using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Core;

public interface IPublisher
{
    JobBuilder.InnerBuilder PublishBuilder<T>(T message);
    
    Task<Guid> Publish(JobBuilder.InnerBuilder jobData);
    
    Task<Guid> Publish(JobParameters jobParameters);
    
    Task<Guid> Publish<T>(T message, JobParameters jobParameters);
    
    Task<Guid> Publish<T>(T message, Action<JobParameters> options) where T : class;
    
    Task<Guid> Publish<T>(T message) where T : class;
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

    public JobBuilder.InnerBuilder PublishBuilder<T>(T message)
    {
        return new JobBuilder()
            .WithMessage(message);
    }

    public async Task<Guid> Publish(JobBuilder.InnerBuilder builder)
    {
        var jobData = builder.Build();
        return await CreateJobAndJobState(jobData.Message, jobData.Type, string.Empty, jobData.ScheduleTime, jobData.MaxRetries,
            jobData.Priority, jobData.ParentId, jobData.State
        );
    }
    public async Task<Guid> Publish(JobParameters jobParameters)
    {
        return await CreateJobAndJobState(jobParameters.Message, jobParameters.Type, string.Empty, jobParameters.ScheduleTime, jobParameters.MaxRetries,
            jobParameters.Priority, jobParameters.ParentId, jobParameters.State
        );
    }

    public Task<Guid> Publish<T>(T message, Action<JobParameters> options) where T : class
    {
        var parameters = new JobParameters();
        options(parameters);
        return CreateJobAndJobState(message, string.Empty, parameters.ScheduleTime, parameters.MaxRetries, parameters.Priority,
            parameters.ParentId);
    }
    
    public Task<Guid> Publish<T>(T message, JobParameters jobParameters)
    {
        return CreateJobAndJobState(jobParameters.Message, jobParameters.Type, string.Empty, jobParameters.ScheduleTime, jobParameters.MaxRetries,
            jobParameters.Priority, jobParameters.ParentId, jobParameters.State
        );
    }

    public async Task<Guid> Publish<T>(T message)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, null,
            null);
    }

    public async Task<Guid> Publish<T>(T message, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, priority,
            null);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, null, null);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, priority, null);
    }

    public async Task<Guid> Publish<T>(T message, int maxRetries) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, null, null);
    }

    public async Task<Guid> Publish<T>(T message, int maxRetries, Priority priority) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, priority, null);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, null, null);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, priority, null);
    }

    public async Task<Guid> Publish<T>(T message, Guid parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, null,
            parentId);
    }

    public async Task<Guid> Publish<T>(T message, Guid parentId, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, priority,
            parentId);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, Guid parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, null, parentId);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, Guid parentId, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, priority,
            parentId);
    }

    public async Task<Guid> Publish<T>(T message, int maxRetries, Guid parentId) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, null, parentId);
    }

    public async Task<Guid> Publish<T>(T message, int maxRetries, Guid parentId, Priority priority) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, priority,
            parentId);
    }

    public async Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Guid parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, null, parentId);
    }

    public Task<Guid> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Guid parentId, Priority priority)
        where T : class
    {
        return CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, priority, parentId);
    }

    private async Task<Guid> CreateJobAndJobState<T>(T message, string name, DateTime? scheduleTime, int? maxRetries,
        Priority? priority, Guid? parentId)
        where T : class
    {
        var jobState = JobHelper.CreateJobAndJobState(message, _configuration.RetryCount, name, scheduleTime,
            maxRetries, priority, parentId, null);

        await _context.Set<JobState>().AddAsync(jobState);

        return jobState.JobId;
    }
    
    private async Task<Guid> CreateJobAndJobState(string message, string type, string name, DateTime? scheduleTime, int? maxRetries,
        Priority? priority, Guid? parentId, State? state)
    {
        var jobState = JobHelper.CreateJobAndJobState(message, type, _configuration.RetryCount, scheduleTime,
            maxRetries, priority, parentId, state);

        await _context.Set<JobState>().AddAsync(jobState);

        return jobState.JobId;
    }
}