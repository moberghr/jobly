using System.Text.Json;
using Handfire.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public interface IPublisher
{
    Task<string> Publish<T>(T message) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime) where T : class;

    Task<string> Publish<T>(T message, int retriedTimes) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, int retriedTimes) where T : class;
}

public class Publisher<TContext> : IPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly int? _retries;    
    public Publisher(TContext context, int? retries)
    {
        _context = context;
        _retries = retries;   
    }

    public async Task<string> Publish<T>(T message)
        where T : class
    {
        return await CreateJobAndJobState<T>(message, name: string.Empty, scheduleTime: null, retriedTimes: null);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime)
        where T : class
    {
        return await CreateJobAndJobState<T>(message, name: string.Empty, scheduleTime, retriedTimes: null);
    }

    public async Task<string> Publish<T>(T message, int retriedTimes) where T : class
    {
        return await CreateJobAndJobState<T>(message, name: string.Empty, scheduleTime: null, retriedTimes);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, int retriedTimes) where T : class
    {
        return await CreateJobAndJobState<T>(message, name: string.Empty, scheduleTime, retriedTimes);
    }

    private async Task<string> CreateJobAndJobState<T>(T message, string name, DateTime? scheduleTime, int? retriedTimes)
        where T : class
    {
        var createdTime = DateTime.UtcNow;

        var jobId = Guid.NewGuid().ToString();

        var job = new Job
        {
            Id = jobId,
            CreateTime = createdTime,
            Message = JsonSerializer.Serialize(message),
            Type = message.GetType().AssemblyQualifiedName!,
            ScheduleTime = scheduleTime,
            CurrentState = Enums.State.Enqueued,
            MaxRetries = retriedTimes ?? _retries ?? 0,
        };

        var jobState = new JobState
        {
            Job = job,
            State = Enums.State.Enqueued,
            DateTime = createdTime,
        };

        await _context.Set<JobState>().AddAsync(jobState);

        return jobId;
    }
}