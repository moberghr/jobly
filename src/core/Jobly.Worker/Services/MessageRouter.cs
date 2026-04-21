using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Routes messages (parent rows with Kind=Message) to handlers by creating one child
/// Kind=Job per registered handler. Wake-up on <c>MessageEnqueued</c> push notifications
/// is routed through <see cref="ServerTaskSignals{TContext}.SignalMessageEnqueued"/>.
/// </summary>
public sealed class MessageRouter<TContext> : IServerTask
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _time;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJoblySqlQueries<TContext> _sqlQueries;
    private readonly JoblyWorkerConfiguration _configuration;

    public MessageRouter(
        TContext context,
        TimeProvider time,
        IServiceScopeFactory scopeFactory,
        IJoblySqlQueries<TContext> sqlQueries,
        IOptions<JoblyWorkerConfiguration> configuration)
    {
        _context = context;
        _time = time;
        _scopeFactory = scopeFactory;
        _sqlQueries = sqlQueries;
        _configuration = configuration.Value;
    }

    public string Name => "MessageRouting";

    public string? LockKey => "jobly:message-routing";

    public TimeSpan? DefaultInterval => _configuration.MessageRoutingInterval;

    public async Task<string?> ExecuteAsync(CancellationToken ct)
    {
        var routed = await RunMessageRoutingAsync(ct);

        return routed > 0 ? $"Routed {routed} messages" : null;
    }

    internal async Task<int> RunMessageRoutingAsync(CancellationToken ct)
    {
        var totalRouted = 0;

        while (true)
        {
            var message = await _sqlQueries.LockNextEnqueuedMessageAsync(_context, ct);

            if (message == null)
            {
                break;
            }

            var messageType = Type.GetType(message.Type!);
            if (messageType == null)
            {
                message.CurrentState = State.Failed;
                _context.Set<JobLog>().Add(new JobLog
                {
                    JobId = message.Id,
                    EventType = "Failed",
                    Timestamp = _time.GetUtcNow().UtcDateTime,
                    Level = "Error",
                    Message = $"Unknown message type: {message.Type}",
                });
                await _context.SaveChangesAsync(ct);

                continue;
            }

            using var handlerScope = _scopeFactory.CreateScope();
            var handlerTypes = JobDispatcher.DiscoverMessageHandlers(messageType, handlerScope.ServiceProvider);

            if (handlerTypes.Count == 0)
            {
                message.CurrentState = State.Failed;
                _context.Set<JobLog>().Add(new JobLog
                {
                    JobId = message.Id,
                    EventType = "Failed",
                    Timestamp = _time.GetUtcNow().UtcDateTime,
                    Level = "Error",
                    Message = $"No handlers registered for message type {messageType.Name}",
                });
                await _context.SaveChangesAsync(ct);

                continue;
            }

            var now = _time.GetUtcNow().UtcDateTime;
            foreach (var handlerType in handlerTypes)
            {
                var job = JobHelper.CreateJob(
                    message: message.Message!,
                    type: message.Type!,
                    scheduleTime: null,
                    queue: message.Queue,
                    parentId: message.Id,
                    state: State.Enqueued,
                    now: now);

                job.HandlerType = handlerType.AssemblyQualifiedName;
                job.TraceId = message.TraceId;
                job.ParentSpanId = message.ParentSpanId;
                job.Metadata = message.Metadata;

                _context.Set<Job>().Add(job);
                _context.Set<JobLog>().Add(new JobLog
                {
                    JobId = job.Id,
                    EventType = "Created",
                    Timestamp = now,
                    Level = "Information",
                    Message = $"Job {job.Id} created from message {message.Id}",
                });
            }

            message.CurrentState = State.Processing;
            await _context.SaveChangesAsync(ct);
            totalRouted++;
        }

        return totalRouted;
    }
}
