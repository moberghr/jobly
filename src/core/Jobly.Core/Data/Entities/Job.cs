using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;

namespace Jobly.Core.Entities;

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string? Type { get; set; }

    public string? Message { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime ScheduleTime { get; set; }

    public State CurrentState { get; set; }

    public int? RecurringJobId { get; set; }

    public int RetriedTimes { get; set; }

    public int MaxRetries { get; set; }

    public string Queue { get; set; } = "default";

    public Guid? ParentJobId { get; set; }

    public RecurringJob? RecurringJob { get; set; }

    public Job? ParentJob { get; set; }

    public List<Job> ChildJobs { get; set; } = [];

    public Batch? Batch { get; set; }

    public Guid? BatchId { get; set; }

    public Guid? CurrentWorkerId { get; set; }

    public string? HandlerType { get; set; }

    public Guid? MessageId { get; set; }

    public Message? MessageEntity { get; set; }

    public DateTime? ExpireAt { get; set; }

    public DateTime? LastKeepAlive { get; set; }

    public Guid? TraceId { get; set; }

    public Guid? SpawnedByJobId { get; set; }
}
