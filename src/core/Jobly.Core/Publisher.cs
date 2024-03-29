using Jobly.Core.Entities;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core;

public interface IPublisher
{
    Task<string> Publish<T>(T message) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime) where T : class;

    Task<string> Publish<T>(T message, int maxRetries) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries) where T : class;

    Task<string> Publish<T>(T message, string parentId) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, string parentId) where T : class;

    Task<string> Publish<T>(T message, int maxRetries, string parentId) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries, string parentId) where T : class;
}

public class Publisher<TContext> : IPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly int _retries;
    public Publisher(TContext context, int retries)
    {
        _context = context;
        _retries = retries;
    }

    public async Task<string> Publish<T>(T message)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, null);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, null);
    }

    public async Task<string> Publish<T>(T message, int maxRetries) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, null);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, null);
    }

    public async Task<string> Publish<T>(T message, string parentId)
       where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries: null, parentId);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, string parentId)
        where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries: null, parentId);
    }

    public async Task<string> Publish<T>(T message, int maxRetries, string parentId) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime: null, maxRetries, parentId);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, int maxRetries, string parentId) where T : class
    {
        return await CreateJobAndJobState(message, name: string.Empty, scheduleTime, maxRetries, parentId);
    }

    private async Task<string> CreateJobAndJobState<T>(T message, string name, DateTime? scheduleTime, int? maxRetries, string? parentId)
        where T : class
    {
        var jobState = JobHelper.CreateJobAndJobState(message, _retries, name, scheduleTime, maxRetries, parentId, null, null);

        await _context.Set<JobState>().AddAsync(jobState);
        await NotifyJob();

        return jobState.JobId;
    }

    // todo: use abstraction for this, this is postgresql specific, could be a part of some notify abstraction
    private async Task NotifyJob()
    {
        await _context.Database.ExecuteSqlRawAsync("NOTIFY job_added;");
    }
}