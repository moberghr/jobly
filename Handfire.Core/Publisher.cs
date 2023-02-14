using System.Text.Json;
using Handfire.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public interface IPublisher
{
    Task<string> Publish<T>(T message, int? jobRetry) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime, int? jobRetry) where T : class;
}

public class Publisher<TContext> : IPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly int _possibleRetrys;    
    public Publisher(TContext context, int possibleRetrys)
    {
        _context = context;
        _possibleRetrys = possibleRetrys;   
    }

    public async Task<string> Publish<T>(T message, int? jobRetry)
        where T : class
    {
        return await CreateJobAndJobState<T>(message, name: string.Empty, scheduleTime: null, jobRetry);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime, int? jobRetry)
        where T : class
    {
        return await CreateJobAndJobState<T>(message, name: string.Empty, scheduleTime, jobRetry);
    }

    private async Task<string> CreateJobAndJobState<T>(T message, string name, DateTime? scheduleTime, int? jobRetry)
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
            PossibleRetries = jobRetry ?? _possibleRetrys,
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