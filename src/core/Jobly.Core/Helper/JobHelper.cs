using System.Text.Json;
using Jobly.Core.Entities;
using Jobly.Core.Enums;

namespace Jobly.Core.Helper;

internal static class JobHelper
{
    public static JobState CreateJobAndJobState<T>(T message, int retries, string name, DateTime? scheduleTime, int? maxRetries, Priority? priority, Guid? parentId, State? state)
        where T : class
    {
        var createdTime = DateTime.UtcNow;
        
        var job = new Job
        {
            CreateTime = createdTime,
            Message = JsonSerializer.Serialize(message),
            Type = message!.GetType().AssemblyQualifiedName!,
            ScheduleTime = scheduleTime ?? createdTime,
            CurrentState = state ?? (parentId == null ? State.Enqueued : State.Awaiting),
            MaxRetries = maxRetries ?? retries,
            Priority = priority ?? Priority.Normal,
            ParentJobId = parentId,
        };

        var jobState = new JobState
        {
            Job = job,
            State = State.Enqueued,
            DateTime = createdTime,
        };

        return jobState;
    }
}
