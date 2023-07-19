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

    public int RetriedTimes { get; set; }

    public int MaxRetries { get; set; }

    public string? ParentJobId { get; set; }

    public RecurringJob? RecurringJob { get; set; }

    public List<JobState> JobStates { get; set; } = new();

    public Job? ParentJob { get; set; }

    public List<Job> ChildJobs { get; set; } = new();

    public string? BatchId { get; set; }

    public Batch? Batch { get; set; }
}
