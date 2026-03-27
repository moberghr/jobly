using System.Text.Json;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Core;

public interface IPublisher
{
    // Queue: create Message (IMessage), immediate routing by worker
    Task<Guid> Publish<T>(T message) where T : class, IMessage;
    Task<Guid> Publish<T>(T message, Priority priority) where T : class, IMessage;

    // Orchestration: create Job directly (IJob)
    Task<Guid> Enqueue<T>(T job) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, Priority priority) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, int maxRetries) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, int maxRetries, Priority priority) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, Guid parentJobId) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, Guid parentJobId, Priority priority) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, int maxRetries, Guid parentJobId) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, int maxRetries, Guid parentJobId, Priority priority) where T : class, IJob;
    Task<Guid> Enqueue<T>(T job, JobParameters jobParameters) where T : class, IJob;

    Task<Guid> Schedule<T>(T job, DateTime scheduleTime) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Priority priority) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Priority priority) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId, Priority priority) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Guid parentJobId) where T : class, IJob;
    Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Guid parentJobId, Priority priority) where T : class, IJob;
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
        return await CreateMessage(message, Priority.Normal);
    }

    public async Task<Guid> Publish<T>(T message, Priority priority) where T : class, IMessage
    {
        return await CreateMessage(message, priority);
    }

    private async Task<Guid> CreateMessage<T>(T message, Priority priority) where T : class, IMessage
    {
        var msg = new Message
        {
            Type = message.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(message),
            Priority = priority,
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

    public async Task<Guid> Enqueue<T>(T job, Priority priority) where T : class, IJob
        => await CreateJob(job, null, null, priority, null);

    public async Task<Guid> Enqueue<T>(T job, int maxRetries) where T : class, IJob
        => await CreateJob(job, null, maxRetries, null, null);

    public async Task<Guid> Enqueue<T>(T job, int maxRetries, Priority priority) where T : class, IJob
        => await CreateJob(job, null, maxRetries, priority, null);

    public async Task<Guid> Enqueue<T>(T job, Guid parentJobId) where T : class, IJob
        => await CreateJob(job, null, null, null, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, Guid parentJobId, Priority priority) where T : class, IJob
        => await CreateJob(job, null, null, priority, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, int maxRetries, Guid parentJobId) where T : class, IJob
        => await CreateJob(job, null, maxRetries, null, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, int maxRetries, Guid parentJobId, Priority priority) where T : class, IJob
        => await CreateJob(job, null, maxRetries, priority, parentJobId);

    public async Task<Guid> Enqueue<T>(T job, JobParameters jobParameters) where T : class, IJob
        => await CreateJob(job, jobParameters.ScheduleTime, jobParameters.MaxRetries, jobParameters.Priority, jobParameters.ParentId);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime) where T : class, IJob
        => await CreateJob(job, scheduleTime, null, null, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Priority priority) where T : class, IJob
        => await CreateJob(job, scheduleTime, null, priority, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries) where T : class, IJob
        => await CreateJob(job, scheduleTime, maxRetries, null, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Priority priority) where T : class, IJob
        => await CreateJob(job, scheduleTime, maxRetries, priority, null);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId) where T : class, IJob
        => await CreateJob(job, scheduleTime, null, null, parentJobId);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId, Priority priority) where T : class, IJob
        => await CreateJob(job, scheduleTime, null, priority, parentJobId);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Guid parentJobId) where T : class, IJob
        => await CreateJob(job, scheduleTime, maxRetries, null, parentJobId);

    public async Task<Guid> Schedule<T>(T job, DateTime scheduleTime, int maxRetries, Guid parentJobId, Priority priority) where T : class, IJob
        => await CreateJob(job, scheduleTime, maxRetries, priority, parentJobId);

    private async Task<Guid> CreateJob<T>(T job, DateTime? scheduleTime, int? maxRetries,
        Priority? priority, Guid? parentId) where T : class, IJob
    {
        var jobState = JobHelper.CreateJobAndJobState(job, _configuration.RetryCount, scheduleTime,
            maxRetries, priority, parentId, null);

        await _context.Set<JobState>().AddAsync(jobState);

        return jobState.JobId;
    }
}
