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
}

public class JobGroupDetailModel : JobGroupModel
{
    public int SpawnedJobsCount { get; set; }

    public Guid? ContinuationJobId { get; set; }
}
