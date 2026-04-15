namespace Jobly.Core.Handlers;

public interface IJobContext : IJobMetadata
{
    Guid JobId { get; }

    Guid TraceId { get; }

    JobFailureOutcome? FailureOutcome { get; set; }
}

public class JobContext : IJobContext
{
    public Guid JobId { get; set; }

    public Guid TraceId { get; set; }

    public JobFailureOutcome? FailureOutcome { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = [];
}
