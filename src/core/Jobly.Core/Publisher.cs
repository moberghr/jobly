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
        var jobState = JobHelper.CreateJobAndJobState(job, _configuration.RetryCount, scheduleTime,
            maxRetries, queue, parentId, null);

        await _context.Set<JobState>().AddAsync(jobState);

        return jobState.JobId;
    }
}
