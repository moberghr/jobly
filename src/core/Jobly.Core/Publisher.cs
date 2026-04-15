using System.Diagnostics;
using System.Text.Json;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jobly.Core;

public interface IPublisher
{
    // Queue: create Message-kind Job (IMessage), immediate routing by worker
    Task<Guid> Publish<T>(T message)
        where T : class, IMessage;

    Task<Guid> Publish<T>(T message, string? queue)
        where T : class, IMessage;

    // Orchestration: create Job directly (IJob)
    Task<Guid> Enqueue<T>(T job)
        where T : class, IJob;

    Task<Guid> Enqueue<T>(T job, string? queue)
        where T : class, IJob;

    Task<Guid> Enqueue<T>(T job, Guid parentJobId)
        where T : class, IJob;

    Task<Guid> Enqueue<T>(T job, Guid parentJobId, string? queue)
        where T : class, IJob;

    Task<Guid> Enqueue<T>(T job, JobParameters jobParameters)
        where T : class, IJob;

    Task<Guid> Schedule<T>(T job, DateTime scheduleTime)
        where T : class, IJob;

    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, string? queue)
        where T : class, IJob;

    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId)
        where T : class, IJob;

    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId, string? queue)
        where T : class, IJob;

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class Publisher<TContext> : IPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly JoblyConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly IServiceProvider _serviceProvider;

    public Publisher(TContext context, IOptions<JoblyConfiguration> configuration, TimeProvider timeProvider, IServiceProvider serviceProvider)
    {
        _context = context;
        _configuration = configuration.Value;
        _timeProvider = timeProvider;
        _serviceProvider = serviceProvider;
    }

    // --- IMessage: create Message-kind Job ---
    public async Task<Guid> Publish<T>(T message)
        where T : class, IMessage
    {
        return await CreateMessage(message);
    }

    public async Task<Guid> Publish<T>(T message, string? queue)
        where T : class, IMessage
    {
        return await CreateMessage(message, queue);
    }

    private async Task<Guid> CreateMessage<T>(T message, string? queue = null)
        where T : class, IMessage
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var publishCtx = await RunPublishPipeline(message, seed: null, CancellationToken.None);

        var msg = new Job
        {
            Kind = JobKind.Message,
            Type = message.GetType().AssemblyQualifiedName!,
            Message = JsonSerializer.Serialize(message),
            Queue = queue ?? "default",
            CreateTime = now,
            ScheduleTime = now,
            CurrentState = State.Enqueued,
            JobCount = 0,
            Metadata = SerializeMetadata(publishCtx.Metadata),
        };

        // Trace propagation: inherit from execution context if inside a handler
        var executionContext = JobExecutionContext.Current;
        if (executionContext != null)
        {
            msg.TraceId = executionContext.TraceId;
            msg.SpawnedByJobId = executionContext.JobId;
        }
        else if (Activity.Current?.TraceId is { } msgActivityTrace)
        {
            msg.TraceId = new Guid(msgActivityTrace.ToHexString());
        }
        else
        {
            msg.TraceId = msg.Id;
        }

        if (Activity.Current?.SpanId is { } msgSpanId && msgSpanId != default)
        {
            msg.ParentSpanId = msgSpanId.ToHexString();
        }

        JoblyTelemetry.JobsEnqueued.Add(1, new KeyValuePair<string, object?>("queue", msg.Queue), new KeyValuePair<string, object?>("kind", "message"));

        await _context.Set<Job>().AddAsync(msg);

        return msg.Id;
    }

    // --- IJob: create Job rows directly ---
    public async Task<Guid> Enqueue<T>(T job)
        where T : class, IJob
        => await CreateJob(job, null, null, null);

    public async Task<Guid> Enqueue<T>(T job, string? queue)
        where T : class, IJob
        => await CreateJob(job, null, queue, null);

    public async Task<Guid> Enqueue<T>(T job, Guid parentJobId)
        where T : class, IJob
        => await CreateJob(job, null, null, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, Guid parentJobId, string? queue)
        where T : class, IJob
        => await CreateJob(job, null, queue, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, JobParameters jobParameters)
        where T : class, IJob
        => await CreateJob(job, jobParameters.ScheduleTime, jobParameters.Queue, jobParameters.ParentId, jobParameters.Mutex, jobParameters.Metadata);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime)
        where T : class, IJob
        => await CreateJob(job, scheduleTime, null, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, string? queue)
        where T : class, IJob
        => await CreateJob(job, scheduleTime, queue, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId)
        where T : class, IJob
        => await CreateJob(job, scheduleTime, null, parentJobId);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId, string? queue)
        where T : class, IJob
        => await CreateJob(job, scheduleTime, queue, parentJobId);

    private async Task<Guid> CreateJob<T>(
        T job,
        DateTime? scheduleTime,
        string? queue,
        Guid? parentId,
        string? mutex = null,
        Dictionary<string, object>? adHocMetadata = null)
        where T : class, IJob
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var publishCtx = await RunPublishPipeline(job, adHocMetadata, CancellationToken.None);

        var newJob = JobHelper.CreateJob(
            job,
            scheduleTime,
            queue,
            parentId,
            null,
            now,
            concurrencyKey: mutex,
            metadata: SerializeMetadata(publishCtx.Metadata));

        // Automatic trace propagation: execution context > parent's trace > self
        var executionContext = JobExecutionContext.Current;
        if (executionContext != null)
        {
            newJob.TraceId = executionContext.TraceId;
            newJob.SpawnedByJobId = executionContext.JobId;
        }
        else if (parentId != null)
        {
            // Inherit trace from parent — check change tracker first (parent may not be committed yet)
            var trackedParent = _context.ChangeTracker.Entries<Job>()
                .FirstOrDefault(e => e.Entity.Id == parentId);
            newJob.TraceId = trackedParent?.Entity.TraceId
                ?? await _context.Set<Job>()
                    .Where(x => x.Id == parentId)
                    .Select(x => x.TraceId)
                    .FirstOrDefaultAsync()
                ?? newJob.Id;
        }
        else if (Activity.Current?.TraceId is { } jobActivityTrace)
        {
            newJob.TraceId = new Guid(jobActivityTrace.ToHexString());
        }
        else
        {
            newJob.TraceId = newJob.Id; // Root of a new trace
        }

        if (Activity.Current?.SpanId is { } jobSpanId && jobSpanId != default)
        {
            newJob.ParentSpanId = jobSpanId.ToHexString();
        }

        JoblyTelemetry.JobsEnqueued.Add(1, new KeyValuePair<string, object?>("queue", newJob.Queue), new KeyValuePair<string, object?>("kind", "job"));

        await _context.Set<Job>().AddAsync(newJob);
        await _context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = newJob.Id,
            EventType = "Created",
            Level = "Information",
            Timestamp = now,
            Message = $"Job created in queue \"{newJob.Queue}\"",
        });

        return newJob.Id;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<PublishContext<T>> RunPublishPipeline<T>(T job, Dictionary<string, object>? seed, CancellationToken ct)
    {
        var metadata = new Dictionary<string, object>();

        // Seed with inherited metadata from parent execution context
        var executionContext = JobExecutionContext.Current;
        if (executionContext?.MetadataJson != null)
        {
            var inherited = JsonSerializer.Deserialize<Dictionary<string, object>>(executionContext.MetadataJson);
            if (inherited != null)
            {
                foreach (var kvp in inherited)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        // Seed with ad-hoc metadata (overrides inherited)
        if (seed != null)
        {
            foreach (var kvp in seed)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        var context = new PublishContext<T> { Job = job, Metadata = metadata };

        var behaviors = _serviceProvider.GetServices<IPublishPipelineBehavior<T>>().ToArray();
        if (behaviors.Length == 0)
        {
            return context;
        }

        PublishDelegate chain = () => Task.CompletedTask;
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next = chain;
            chain = () => behavior.PublishAsync(context, next, ct);
        }

        await chain();
        return context;
    }

    private static string? SerializeMetadata(Dictionary<string, object> metadata)
    {
        return metadata.Count > 0 ? JsonSerializer.Serialize(metadata) : null;
    }
}
