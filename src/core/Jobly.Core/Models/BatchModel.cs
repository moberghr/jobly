using Jobly.Core.Enums;

namespace Jobly.Core.Models;

public class BatchModel
{
    public Guid Id { get; set; }

    public int TotalJobs { get; set; }

    public int RemainingJobs { get; set; }

    public State PlaceholderState { get; set; }

    public DateTime CreateTime { get; set; }
}

public class BatchDetailModel : BatchModel
{
    public List<JobModel> Jobs { get; set; } = [];

    public Guid? ContinuationJobId { get; set; }
}
