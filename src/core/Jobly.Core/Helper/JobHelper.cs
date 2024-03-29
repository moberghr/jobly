using System.Text.Json;
using Jobly.Core.Entities;
using Jobly.Core.Enums;

namespace Jobly.Core.Helper;

internal static class JobHelper
{
    public static JobState CreateJobAndJobState<T>(T message, int retries, string name, DateTime? scheduleTime, int? maxRetries, string? parentId, State? state, string? batchId)
        where T : class
    {
        var createdTime = DateTime.UtcNow;

        var jobId = Guid.NewGuid().ToString();

        var job = new Job
        {
            Id = jobId,
            CreateTime = createdTime,
            Message = JsonSerializer.Serialize(message),
            Type = message!.GetType().AssemblyQualifiedName!,
            ScheduleTime = scheduleTime ?? createdTime,
            CurrentState = state != null ? state.Value : string.IsNullOrEmpty(parentId) ? State.Enqueued : State.Awaiting,
            MaxRetries = maxRetries ?? retries,
            ParentJobId = parentId,
            BatchId = batchId,
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
