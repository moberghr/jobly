using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Xml.Linq;
using Cronos;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public interface IPublisher
{
    Task<string> Publish<T>(T message) where T : class;

    Task<string> Publish<T>(T message, DateTime scheduleTime) where T : class;
}

public class Publisher<TContext> : IPublisher
    where TContext : DbContext
{
    private readonly TContext _context;

    public Publisher(TContext context)
    {
        _context = context;
    }

    public async Task<string> Publish<T>(T message)
        where T : class
    {
        return await CreateJobAndJobState<T>(message, name: string.Empty, scheduleTime: null);
    }

    public async Task<string> Publish<T>(T message, DateTime scheduleTime)
        where T : class
    {
        return await CreateJobAndJobState<T>(message, name: string.Empty, scheduleTime);
    }

    private async Task<string> CreateJobAndJobState<T>(T message, string name, DateTime? scheduleTime)
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
            CurrentState = Enums.State.Created
        };

        var jobState = new JobState
        {
            Job = job,
            State = Enums.State.Created,
            DateTime = createdTime,
        };

        await _context.Set<JobState>().AddAsync(jobState);

        return jobId;
    }
}