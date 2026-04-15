using System.Text.Json;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;

namespace Jobly.Core.Helper;

internal static class JobHelper
{
    private static Job CreateJobInternal(string message, string type, DateTime? scheduleTime, string? queue, Guid? parentId, State? state, DateTime now, string? concurrencyKey = null, string? metadata = null)
    {
        var job = new Job
        {
            CreateTime = now,
            Message = message,
            Type = type,
            ScheduleTime = scheduleTime ?? now,
            CurrentState = state ?? (parentId == null ? State.Enqueued : State.Awaiting),
            Queue = queue ?? "default",
            ParentJobId = parentId,
            ConcurrencyKey = concurrencyKey,
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
        string? concurrencyKey = null,
        string? metadata = null)
        where T : class, IJob
    {
        var serializedMessage = JsonSerializer.Serialize(message);
        var type = message!.GetType().AssemblyQualifiedName!;

        return CreateJobInternal(serializedMessage, type, scheduleTime, queue, parentId, state, now, concurrencyKey, metadata);
    }

    public static Job CreateJob(string message, string type, DateTime? scheduleTime, string? queue, Guid? parentId, State? state, DateTime now, string? concurrencyKey = null, string? metadata = null)
    {
        return CreateJobInternal(message, type, scheduleTime, queue, parentId, state, now, concurrencyKey, metadata);
    }
}
