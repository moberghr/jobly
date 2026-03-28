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

    public List<JobModel> SiblingJobs { get; set; } = [];

    public List<JobModel> ChildJobs { get; set; } = [];

    public Guid? TraceId { get; set; }

    public Guid? SpawnedByJobId { get; set; }

    public List<JobModel> TraceJobs { get; set; } = [];
}
