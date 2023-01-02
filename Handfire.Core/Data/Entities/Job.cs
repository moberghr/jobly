using Handfire.Core.Data.Entities;
using Handfire.Core.Enums;

namespace Handfire.Core.Entities;

public class Job
{
    public int Id { get; set; }

    public string Type { get; set; }

    public string Message { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime? ScheduleTime { get; set; }

    public DateTime? ProcessedTime { get; set; }

    public State CurrentState { get; set; }

    public bool IsRecurringJob { get; set; }

    public RecurringJob RecurringJob { get; set; }

    public ICollection<JobState> JobStates { get; set; }
}
