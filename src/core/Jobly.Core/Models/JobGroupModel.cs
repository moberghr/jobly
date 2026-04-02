using Jobly.Core.Enums;

namespace Jobly.Core.Models;

public class JobGroupModel
{
    public Guid Id { get; set; }

    public JobKind Kind { get; set; }

    public State CurrentState { get; set; }

    public int JobCount { get; set; }

    public DateTime CreateTime { get; set; }

    // Message-specific
    public string? Type { get; set; }

    public string? Payload { get; set; }

    public string? Queue { get; set; }

    // Batch-specific
    public int TotalJobs { get; set; }

    public ContinuationOptions? ContinuationOptions { get; set; }

    public int CompletedJobs { get; set; }

    public int FailedJobs { get; set; }
}

public class JobGroupDetailModel : JobGroupModel
{
    public Guid? ParentJobId { get; set; }

    public JobKind? ParentJobKind { get; set; }

    public int SpawnedJobsCount { get; set; }

    public List<ContinuationInfo> Continuations { get; set; } = [];
}

public class ContinuationInfo
{
    public Guid Id { get; set; }

    public JobKind Kind { get; set; }

    public State CurrentState { get; set; }

    public string? Type { get; set; }

    public string? HandlerType { get; set; }
}
