namespace Jobly.Core.Handlers;

public interface IJobContext : IJobMetadata
{
    Guid JobId { get; }

    Guid TraceId { get; }
}

public class JobContext : IJobContext
{
    public Guid JobId { get; set; }

    public Guid TraceId { get; set; }

    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}
