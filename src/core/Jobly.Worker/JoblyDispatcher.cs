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
    private readonly TimeProvider _timeProvider;
    private readonly Channel<Job> _jobChannel;
    private readonly int _workerCount;

    public JoblyDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<JoblyDispatcher<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        WorkerGroupConfiguration groupConfiguration,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _groupConfiguration = groupConfiguration;
        _timeProvider = timeProvider;
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

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Fetch only Kind=Job (messages are routed by MessageRoutingTask)
        var jobs = await context.Set<Job>()
            .Where(x => x.Kind == JobKind.Job && x.CurrentState == State.Enqueued && x.ScheduleTime < now)
            .Where(x => _groupConfiguration.Queues.Contains(x.Queue))
            .OrderBy(x => x.Queue)
            .ThenBy(x => x.ScheduleTime)
            .Take(available)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .ToListAsync(ct);

        if (jobs.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return false;
        }

        // Batch mark all fetched jobs as Processing
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

        // Note: mutex check for dispatcher mode happens in JoblyDispatcherWorker.ProcessJob
        // via distributed lock, not here in the batch fetch.
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        foreach (var job in jobs)
        {
            await _jobChannel.Writer.WriteAsync(job, ct);
        }

        return true;
    }
}
