using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Core;

public interface IPublisher
{
    Task<string> Publish<T>(T message) where T : class;
    Task<string> Publish<T>(T message, Priority priority) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, Priority priority) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries, string parentId) where T : class;

    Task<string> Publish<T>(T message, int maxRetries) where T : class;

    Task<string> Publish<T>(T message, int maxRetries, Priority priority) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Priority priority) where T : class;

    Task<string> Publish<T>(T message, string parentId) where T : class;
    Task<string> Publish<T>(T message, string parentId, Priority priority) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, string parentId) where T : class;
    Task<string> Publish<T>(T message, DateTime scheduleTime, string parentId, Priority priority) where T : class;

    Task<string> Publish<T>(T message, int maxRetries, string parentId) where T : class;
    Task<string> Publish<T>(T message, int maxRetries, string parentId, Priority priority) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries, string parentId, Priority priority)
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

    public async Task<string> Publish<T>(T message)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, null,
            null);
    }

    public async Task<string> Publish<T>(T message, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, priority,
            null);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, null, null);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, priority, null);
    }

    public async Task<string> Publish<T>(T message, int maxRetries) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, null, null);
    }

    public async Task<string> Publish<T>(T message, int maxRetries, Priority priority) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, priority, null);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, null, null);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, priority, null);
    }

    public async Task<string> Publish<T>(T message, string parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, null,
            parentId);
    }

    public async Task<string> Publish<T>(T message, string parentId, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, priority,
            parentId);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, string parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, null, parentId);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, string parentId, Priority priority)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, priority,
            parentId);
    }

    public async Task<string> Publish<T>(T message, int maxRetries, string parentId) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, null, parentId);
    }

    public async Task<string> Publish<T>(T message, int maxRetries, string parentId, Priority priority) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, priority,
            parentId);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries, string parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, null, parentId);
    }

    public Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries, string parentId, Priority priority)
        where T : class
    {
        return CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, priority, parentId);
    }

    private async Task<string> CreateJobAndJobState<T>(T message, string name, DateTime? scheduleTime, int? maxRetries,
        Priority? priority, string? parentId)
        where T : class
    {
        var jobState = JobHelper.CreateJobAndJobState(message, _configuration.RetryCount, name, scheduleTime,
            maxRetries, priority, parentId, null);

        await _context.Set<JobState>().AddAsync(jobState);

        return jobState.JobId;
    }
}