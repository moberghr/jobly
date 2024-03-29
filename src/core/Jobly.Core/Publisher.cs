using Jobly.Core.Entities;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    private readonly IJoblyNotifer? _notifier;
    
    public Publisher(TContext context, IConfigureOptions<JoblyConfiguration> configuration, IServiceProvider serviceProvider)
    {
        var options = configuration.ConfigureDefault();
        _retries = options.RetryCount;
        _context = context;
        _notifier = serviceProvider.GetService<IJoblyNotifer>();
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
        await NotifyJob(jobState.Job);

        return jobState.JobId;
    }
    
    private async Task NotifyJob(Job job)
    {
        // todo: when we add priority, do not send notification for low priority jobs
        if (_notifier != null)
        {
            await _notifier!.NotifyAsync(job);
        }
    }
}