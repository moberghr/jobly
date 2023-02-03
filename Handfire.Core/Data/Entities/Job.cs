using System.ComponentModel.DataAnnotations;
using Handfire.Core.Data.Entities;
using Handfire.Core.Enums;

namespace Handfire.Core.Entities;

public class Job
{
    [MaxLength(50)]
    public string Id { get; set; }

    public string Type { get; set; }

    public string Message { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime? ScheduleTime { get; set; }

    public State CurrentState { get; set; }

    public int? RecurringJobId { get; set; }

    public RecurringJob? RecurringJob { get; set; }

    public List<JobState> JobStates { get; set; } = new();
}
