using System.Text.Json;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Core;

public interface IPublisher
{
    // Queue: create Message (IMessage), immediate routing by worker
    Task<Guid> Publish<T>(T message) where T : class, IMessage;
    Task<Guid> Publish<T>(T message, string? queue) where T : class, IMessage;

    // Orchestration: create Job directly (IJob)
    Task<Guid> Enqueue<T>(T job) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, string? queue) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, int maxRetries) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, int maxRetries, string? queue) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, Guid parentJobId) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, Guid parentJobId, string? queue) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, int maxRetries, Guid parentJobId) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, int maxRetries, Guid parentJobId, string? queue) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, JobParameters jobParameters) where T : class, IJob;

    Task<Guid> Schedule<T>(T job, DateTime scheduleTime) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, string? queue) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, string? queue) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId, string? queue) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Guid parentJobId) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Guid parentJobId, string? queue) where T : class, IJob;
}

public class Publisher<TContext> : IPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly JoblyConfiguration _configuration;

    public Publisher(TContext context, IOptions<JoblyConfiguration> configuration)
    {
        _context = context;
        _configuration = configuration.Value;
    }

    // --- IMessage: create Message rows ---

    public async Task<Guid> Publish<T>(T message) where T : class, IMessage
    {
        return await CreateMessage(message);
    }

    public async Task<Guid> Publish<T>(T message, string? queue) where T : class, IMessage
    {
        return await CreateMessage(message, queue);
    }

    private async Task<Guid> CreateMessage<T>(T message, string? queue = null) where T : class, IMessage
    {
        var msg = new Message
        {
            Type = message.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(message),
            Queue = queue ?? "default",
            CreateTime = DateTime.UtcNow,
            CurrentState = State.Enqueued,
            JobCount = 0
        };

        await _context.Set<Message>().AddAsync(msg);

        return msg.Id;
    }

    // --- IJob: create Job rows directly ---

    public async Task<Guid> Enqueue<T>(T job) where T : class, IJob
        => await CreateJob(job, null, null, null, null);

    public async Task<Guid> Enqueue<T>(T job, string? queue) where T : class, IJob
        => await CreateJob(job, null, null, queue, null);

    public async Task<Guid> Enqueue<T>(T job, int maxRetries) where T : class, IJob
        => await CreateJob(job, null, maxRetries, null, null);

    public async Task<Guid> Enqueue<T>(T job, int maxRetries, string? queue) where T : class, IJob
        => await CreateJob(job, null, maxRetries, queue, null);

    public async Task<Guid> Enqueue<T>(T job, Guid parentJobId) where T : class, IJob
        => await CreateJob(job, null, null, null, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, Guid parentJobId, string? queue) where T : class, IJob
        => await CreateJob(job, null, null, queue, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, int maxRetries, Guid parentJobId) where T : class, IJob
        => await CreateJob(job, null, maxRetries, null, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, int maxRetries, Guid parentJobId, string? queue) where T : class, IJob
        => await CreateJob(job, null, maxRetries, queue, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, JobParameters jobParameters) where T : class, IJob
        => await CreateJob(job, jobParameters.ScheduleTime, jobParameters.MaxRetries, jobParameters.Queue, jobParameters.ParentId);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime) where T : class, IJob
        => await CreateJob(job, scheduleTime, null, null, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, string? queue) where T : class, IJob
        => await CreateJob(job, scheduleTime, null, queue, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries) where T : class, IJob
        => await CreateJob(job, scheduleTime, maxRetries, null, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, string? queue) where T : class, IJob
        => await CreateJob(job, scheduleTime, maxRetries, queue, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId) where T : class, IJob
        => await CreateJob(job, scheduleTime, null, null, parentJobId);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId, string? queue) where T : class, IJob
        => await CreateJob(job, scheduleTime, null, queue, parentJobId);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Guid parentJobId) where T : class, IJob
        => await CreateJob(job, scheduleTime, maxRetries, null, parentJobId);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Guid parentJobId, string? queue) where T : class, IJob
        => await CreateJob(job, scheduleTime, maxRetries, queue, parentJobId);

    private async Task<Guid> CreateJob<T>(T job, DateTime? scheduleTime, int? maxRetries,
        string? queue, Guid? parentId) where T : class, IJob
    {
        var newJob = JobHelper.CreateJob(job, _configuration.RetryCount, scheduleTime,
            maxRetries, queue, parentId, null);

        // Automatic trace propagation: if called from within a job handler, inherit trace
        var executionContext = JobExecutionContext.Current;
        if (executionContext != null)
        {
            newJob.TraceId = executionContext.TraceId;
            newJob.SpawnedByJobId = executionContext.JobId;
        }
        else
        {
            newJob.TraceId = newJob.Id; // Root of a new trace
        }

        await _context.Set<Job>().AddAsync(newJob);
        await _context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = newJob.Id,
            EventType = "Created",
            Level = "Information",
            Timestamp = DateTime.UtcNow,
            Message = $"Job created in queue \"{newJob.Queue}\""
        });

        return newJob.Id;
    }
}
