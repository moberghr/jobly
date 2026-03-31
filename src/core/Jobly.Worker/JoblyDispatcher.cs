using System.Threading.Channels;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Batch-fetches jobs from the database and distributes them to worker slots via a channel.
/// One dispatcher per worker group. Workers execute handlers and complete jobs individually.
/// </summary>
public class JoblyDispatcher<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JoblyDispatcher<TContext>> _logger;
    private readonly WorkerGroupConfiguration _groupConfiguration;
    private readonly Channel<Job> _jobChannel;
    private readonly int _workerCount;

    public JoblyDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<JoblyDispatcher<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        WorkerGroupConfiguration groupConfiguration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _groupConfiguration = groupConfiguration;
        _workerCount = groupConfiguration.WorkerCount;

        _jobChannel = Channel.CreateBounded<Job>(new BoundedChannelOptions(_workerCount)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
        });
    }

    public ChannelReader<Job> JobReader => _jobChannel.Reader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var fetched = await FetchAndDistribute(stoppingToken);
                if (!fetched)
                {
                    await Task.Delay(_groupConfiguration.PollingInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatcher fetch failed");
                await Task.Delay(_groupConfiguration.PollingInterval, stoppingToken);
            }
        }

        _jobChannel.Writer.Complete();
    }

    private async Task<bool> FetchAndDistribute(CancellationToken ct)
    {
        var available = _workerCount - _jobChannel.Reader.Count;
        if (available <= 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // Batch fetch — single query, single index scan, locks N rows at once
        var jobs = await context.Set<Job>()
            .Where(x => x.CurrentState == State.Enqueued)
            .Where(x => x.ScheduleTime < DateTime.UtcNow)
            .Where(x => _groupConfiguration.Queues.Contains(x.Queue))
            .OrderBy(x => x.Queue)
            .ThenBy(x => x.ScheduleTime)
            .Take(available)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .ToListAsync(ct);

        // Always try to route messages
        {
            var messages = await context.Set<Message>()
                .Where(x => x.CurrentState == State.Enqueued)
                .Where(x => _groupConfiguration.Queues.Contains(x.Queue))
                .OrderBy(x => x.Queue)
                .ThenBy(x => x.CreateTime)
                .Take(available)
                .TagWith(InterceptorConstants.RowLockTableMessage)
                .ToListAsync(ct);

            foreach (var message in messages)
            {
                var messageType = Type.GetType(message.Type);
                if (messageType == null)
                {
                    continue;
                }

                using var handlerScope = _scopeFactory.CreateScope();
                var handlerTypes = Jobly.Core.Handlers.JobDispatcher.DiscoverMessageHandlers(messageType, handlerScope.ServiceProvider);

                if (handlerTypes.Count == 0)
                {
                    message.CurrentState = State.Failed;
                    continue;
                }

                var messageTraceId = Guid.NewGuid();
                foreach (var handlerType in handlerTypes)
                {
                    var job = Jobly.Core.Helper.JobHelper.CreateJob(
                        message: message.Payload,
                        type: message.Type,
                        retries: 0,
                        scheduleTime: null,
                        maxRetries: 0,
                        queue: message.Queue,
                        parentId: null,
                        state: State.Enqueued);

                    job.HandlerType = handlerType.AssemblyQualifiedName;
                    job.MessageId = message.Id;
                    job.TraceId = messageTraceId;

                    context.Set<Job>().Add(job);
                    context.Set<JobLog>().Add(new JobLog
                    {
                        JobId = job.Id,
                        EventType = "Created",
                        Timestamp = DateTime.UtcNow,
                        Level = "Information",
                        Message = $"Job {job.Id} created from message {message.Id}",
                    });
                }

                message.JobCount = handlerTypes.Count;
                message.CurrentState = State.Processing;
            }
        }

        if (jobs.Count == 0)
        {
            // No jobs found, but messages may have been routed — save and return
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return false;
        }

        // Batch mark all fetched jobs as Processing
        var now = DateTime.UtcNow;
        foreach (var job in jobs)
        {
            job.CurrentState = State.Processing;
            job.LastKeepAlive = now;

            context.Set<JobLog>().Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Processing",
                Timestamp = now,
                Level = "Information",
                Message = $"The job {job.Id} is being processed",
            });

        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        foreach (var job in jobs)
        {
            await _jobChannel.Writer.WriteAsync(job, ct);
        }

        return true;
    }

}
