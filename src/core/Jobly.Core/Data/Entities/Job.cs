using System.ComponentModel.DataAnnotations;
using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;

namespace Jobly.Core.Entities;

public class Job
{
    [MaxLength(50)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Type { get; set; }

    public string Message { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime ScheduleTime { get; set; }

    public State CurrentState { get; set; }

    public int? RecurringJobId { get; set; }

    public int RetriedTimes { get; set; }

    public int MaxRetries { get; set; }
    
    public required Priority Priority { get; set; }

    public Guid? ParentJobId { get; set; }

    public RecurringJob? RecurringJob { get; set; }

    public List<JobState> JobStates { get; set; } = new();

    public Job? ParentJob { get; set; }

    public List<Job> ChildJobs { get; set; } = new();

    public Batch? Batch { get; set; }
    
    public Guid? BatchId { get; set; }
    
    public Guid? CurrentServerId { get; set; }

    public Guid? CurrentWorkerId { get; set; }
}
