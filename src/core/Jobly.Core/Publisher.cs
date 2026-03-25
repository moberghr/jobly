using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Core;

public interface IPublisher
{
    Task<Guid> Publish<T>(T message) where T : class;

    Task<Guid> Publish<T>(T message, JobParameters jobParameters) where T : class;

    Task<Guid> Publish<T>(T message, Priority priority) where T : class;

    Task<Guid> Publish<T>(T message, DateTime scheduleTime) where T : class;

    Task<Guid> Publish<T>(T message, DateTime scheduleTime, Priority priority) where T : class;

    Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Guid parentId) where T : class;

    Task<Guid> Publish<T>(T message, int maxRetries) where T : class;

    Task<Guid> Publish<T>(T message, int maxRetries, Priority priority) where T : class;

    Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries) where T : class;

    Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Priority priority) where T : class;

    Task<Guid> Publish<T>(T message, Guid parentId) where T : class;
    Task<Guid> Publish<T>(T message, Guid parentId, Priority priority) where T : class;

    Task<Guid> Publish<T>(T message, DateTime scheduleTime, Guid parentId) where T : class;
    Task<Guid> Publish<T>(T message, DateTime scheduleTime, Guid parentId, Priority priority) where T : class;

    Task<Guid> Publish<T>(T message, int maxRetries, Guid parentId) where T : class;
    Task<Guid> Publish<T>(T message, int maxRetries, Guid parentId, Priority priority) where T : class;

    Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Guid parentId, Priority priority)
        where T : class;
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


    public async Task<Guid> Publish<T>(T message)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime: null, maxRetries: null, priority: null,
            parentId: null);
    }

    public Task<Guid> Publish<T>(T message, JobParameters jobParameters) where T : class
    {
        return CreateJobAndJobState(message,
            jobParameters.ScheduleTime,
            jobParameters.MaxRetries,
            jobParameters.Priority,
            jobParameters.ParentId
        );
    }

    public async Task<Guid> Publish<T>(T message, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime: null, maxRetries: null, priority: priority,
            parentId: null);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime, maxRetries: null, priority: null, parentId: null);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime, maxRetries: null, priority: priority, parentId: null);
    }

    public async Task<Guid> Publish<T>(T message, int maxRetries) where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime: null, maxRetries: maxRetries, priority: null, parentId: null);
    }

    public async Task<Guid> Publish<T>(T message, int maxRetries, Priority priority) where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime: null, maxRetries: maxRetries, priority: priority, parentId: null);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries) where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime, maxRetries, null, null);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime, maxRetries, priority, null);
    }

    public async Task<Guid> Publish<T>(T message, Guid parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime: null, maxRetries: null, priority: null,
            parentId: parentId);
    }

    public async Task<Guid> Publish<T>(T message, Guid parentId, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime: null, maxRetries: null, priority: priority,
            parentId: parentId);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, Guid parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime, maxRetries: null, priority: null, parentId: parentId);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, Guid parentId, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime, maxRetries: null, priority: priority,
            parentId: parentId);
    }

    public async Task<Guid> Publish<T>(T message, int maxRetries, Guid parentId) where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime: null, maxRetries: maxRetries, priority: null, parentId: parentId);
    }

    public async Task<Guid> Publish<T>(T message, int maxRetries, Guid parentId, Priority priority) where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime: null, maxRetries: maxRetries, priority: priority,
            parentId: parentId);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Guid parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, scheduleTime, maxRetries, null, parentId);
    }

    public Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Guid parentId, Priority priority)
        where T : class
    {
        return CreateJobAndJobState(message, scheduleTime, maxRetries, priority, parentId);
    }

    private async Task<Guid> CreateJobAndJobState<T>(T message, DateTime? scheduleTime, int? maxRetries,
        Priority? priority, Guid? parentId)
        where T : class
    {
        var jobState = JobHelper.CreateJobAndJobState(message, _configuration.RetryCount, scheduleTime,
            maxRetries, priority, parentId, null);

        await _context.Set<JobState>().AddAsync(jobState);

        return jobState.JobId;
    }
}