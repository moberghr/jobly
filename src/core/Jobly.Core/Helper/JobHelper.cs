using System.Text.Json;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;

namespace Jobly.Core.Helper;

internal static class JobHelper
{
    private static Job CreateJobInternal(string message, string type, DateTime? scheduleTime, string? queue, Guid? parentId, State? state, DateTime now, string? metadata = null)
    {
        var effectiveScheduleTime = scheduleTime ?? now;
        var job = new Job
        {
            CreateTime = now,
            Message = message,
            Type = type,
            ScheduleTime = effectiveScheduleTime,
            CurrentState = state ?? DefaultState(parentId, effectiveScheduleTime, now),
            Queue = queue ?? "default",
            ParentJobId = parentId,
            Metadata = metadata,
        };

        return job;
    }

    public static Job CreateJob<T>(
        T message,
        DateTime? scheduleTime,
        string? queue,
        Guid? parentId,
        State? state,
        DateTime now,
        string? metadata = null)
        where T : class, IJob
    {
        var serializedMessage = JsonSerializer.Serialize(message);
        var type = message!.GetType().AssemblyQualifiedName!;

        return CreateJobInternal(serializedMessage, type, scheduleTime, queue, parentId, state, now, metadata);
    }

    public static Job CreateJob(string message, string type, DateTime? scheduleTime, string? queue, Guid? parentId, State? state, DateTime now, string? metadata = null)
    {
        return CreateJobInternal(message, type, scheduleTime, queue, parentId, state, now, metadata);
    }

    private static State DefaultState(Guid? parentId, DateTime scheduleTime, DateTime now)
    {
        if (parentId != null)
        {
            return State.Awaiting;
        }

        if (scheduleTime > now)
        {
            return State.Scheduled;
        }

        return State.Enqueued;
    }
}
