namespace Jobly.Core.Models;

public class JobDetailModel : JobModel
{
    public string? HandlerType { get; set; }

    public Guid? MessageId { get; set; }

    public Guid? ParentJobId { get; set; }

    public Guid? BatchId { get; set; }

    public int RetriedTimes { get; set; }

    public int MaxRetries { get; set; }

    public List<JobLogModel> Logs { get; set; } = [];

    public int SiblingJobCount { get; set; }

    public int ChildJobCount { get; set; }

    public Guid? TraceId { get; set; }

    public Guid? SpawnedByJobId { get; set; }

    public int TraceJobCount { get; set; }
}
