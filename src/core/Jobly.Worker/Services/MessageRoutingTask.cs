using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Interceptors;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

public class MessageRoutingTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MessageRoutingTask(
        IServiceScopeFactory scopeFactory,
        ILogger<MessageRoutingTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IDistributedLockProvider lockProvider,
        TimeProvider timeProvider)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:message-routing", lockProvider)
    {
        _scopeFactory = scopeFactory;
    }

    protected override string TaskName => "MessageRouting";

    protected override TimeSpan DefaultInterval => Configuration.MessageRoutingInterval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var routed = await RunMessageRouting(context, _scopeFactory, TimeProvider, ct);
        return routed > 0 ? $"Routed {routed} messages" : null;
    }

    /// <summary>
    /// Routes all pending messages. Creates child jobs for each handler.
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task<int> RunMessageRouting<TCtx>(TCtx context, IServiceScopeFactory scopeFactory, TimeProvider timeProvider, CancellationToken ct)
        where TCtx : DbContext
    {
        var totalRouted = 0;

        while (true)
        {
            var message = await context.Set<Job>()
                .Where(x => x.Kind == JobKind.Message && x.CurrentState == State.Enqueued)
                .OrderBy(x => x.Queue)
                .ThenBy(x => x.ScheduleTime)
                .TagWith(InterceptorConstants.RowLockTableJob)
                .FirstOrDefaultAsync(ct);

            if (message == null)
            {
                break;
            }

            var messageType = Type.GetType(message.Type!);
            if (messageType == null)
            {
                message.CurrentState = State.Failed;
                context.Set<JobLog>().Add(new JobLog
                {
                    JobId = message.Id,
                    EventType = "Failed",
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                    Level = "Error",
                    Message = $"Unknown message type: {message.Type}",
                });
                await context.SaveChangesAsync(ct);
                continue;
            }

            using var handlerScope = scopeFactory.CreateScope();
            var handlerTypes = JobDispatcher.DiscoverMessageHandlers(messageType, handlerScope.ServiceProvider);

            if (handlerTypes.Count == 0)
            {
                message.CurrentState = State.Failed;
                context.Set<JobLog>().Add(new JobLog
                {
                    JobId = message.Id,
                    EventType = "Failed",
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                    Level = "Error",
                    Message = $"No handlers registered for message type {messageType.Name}",
                });
                await context.SaveChangesAsync(ct);
                continue;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var handlerType in handlerTypes)
            {
                var job = JobHelper.CreateJob(
                    message: message.Message!,
                    type: message.Type!,
                    retries: 0,
                    scheduleTime: null,
                    maxRetries: 0,
                    queue: message.Queue,
                    parentId: message.Id,
                    state: State.Enqueued,
                    now: now);

                job.HandlerType = handlerType.AssemblyQualifiedName;
                job.TraceId = message.TraceId;
                job.Metadata = message.Metadata;

                context.Set<Job>().Add(job);
                context.Set<JobLog>().Add(new JobLog
                {
                    JobId = job.Id,
                    EventType = "Created",
                    Timestamp = now,
                    Level = "Information",
                    Message = $"Job {job.Id} created from message {message.Id}",
                });
            }

            message.CurrentState = State.Processing;
            await context.SaveChangesAsync(ct);
            totalRouted++;
        }

        return totalRouted;
    }
}
