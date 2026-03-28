using System.Text.Json;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;

namespace Jobly.Core.Helper;

public static class JobHelper
{
    private static Job CreateJobInternal(string message, string type, int retries, DateTime? scheduleTime, int? maxRetries, string? queue, Guid? parentId, State? state, int? recurringJobId)
    {
        var createdTime = DateTime.UtcNow;

        var job = new Job
        {
            CreateTime = createdTime,
            Message = message,
            Type = type,
            ScheduleTime = scheduleTime ?? createdTime,
            CurrentState = state ?? (parentId == null ? State.Enqueued : State.Awaiting),
            MaxRetries = maxRetries ?? retries,
            Queue = queue ?? "default",
            ParentJobId = parentId,
            RecurringJobId = recurringJobId,
        };

        return job;
    }

    public static Job CreateJob<T>(T message, int retries, DateTime? scheduleTime, int? maxRetries,
        string? queue, Guid? parentId, State? state)
        where T : class, IJob
    {
        var serializedMessage = JsonSerializer.Serialize(message);
        var type = message!.GetType().AssemblyQualifiedName!;
        return CreateJobInternal(serializedMessage, type, retries, scheduleTime, maxRetries, queue, parentId, state, null);
    }

    public static Job CreateJob(string message, string type, int retries, DateTime? scheduleTime, int? maxRetries, string? queue, Guid? parentId, State? state)
    {
        return CreateJobInternal(message, type, retries, scheduleTime, maxRetries, queue, parentId, state, null);
    }

    public static Job CreateJob(string message, string type, int retries, DateTime? scheduleTime, int? maxRetries, string? queue, Guid? parentId, int recurringJobId, State? state)
    {
        return CreateJobInternal(message, type, retries, scheduleTime, maxRetries, queue, parentId, state, recurringJobId);
    }
}
