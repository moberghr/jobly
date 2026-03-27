using Jobly.Core.Enums;

namespace Jobly.Core.Models;

public class JobDetailModel : JobModel
{
    public string? HandlerType { get; set; }

    public Guid? MessageId { get; set; }

    public Guid? ParentJobId { get; set; }

    public Guid? BatchId { get; set; }

    public int RetriedTimes { get; set; }

    public int MaxRetries { get; set; }

    public List<JobStateModel> StateHistory { get; set; } = new();
}
