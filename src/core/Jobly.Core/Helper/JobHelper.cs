using System.Text.Json;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;

namespace Jobly.Core.Helper;

public static class JobHelper
{
    private static JobState CreateJobAndJobStateInternal(string message, string type, int retries, DateTime? scheduleTime, int? maxRetries, string? queue, Guid? parentId, State? state, int? recurringJobId)
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

        var jobState = new JobState
        {
            Job = job,
            State = State.Enqueued,
            DateTime = createdTime,
        };

        return jobState;
    }

    public static JobState CreateJobAndJobState<T>(T message, int retries, DateTime? scheduleTime, int? maxRetries,
        string? queue, Guid? parentId, State? state)
        where T : class, IJob
    {
        var serializedMessage = JsonSerializer.Serialize(message);
        var type = message!.GetType().AssemblyQualifiedName!;
        return CreateJobAndJobStateInternal(serializedMessage, type, retries, scheduleTime, maxRetries, queue, parentId, state, null);
    }

    public static JobState CreateJobAndJobState(string message, string type, int retries, DateTime? scheduleTime, int? maxRetries, string? queue, Guid? parentId, State? state)
    {
        return CreateJobAndJobStateInternal(message, type, retries, scheduleTime, maxRetries, queue, parentId, state, null);
    }

    public static JobState CreateJobAndJobState(string message, string type, int retries, DateTime? scheduleTime, int? maxRetries, string? queue, Guid? parentId, int recurringJobId, State? state)
    {
        return CreateJobAndJobStateInternal(message, type, retries, scheduleTime, maxRetries, queue, parentId, state, recurringJobId);
    }
}
