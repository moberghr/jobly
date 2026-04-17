namespace Jobly.Core.Handlers;

public interface IJobContext
{
    Guid JobId { get; }

    Guid TraceId { get; }

    Type? HandlerType { get; }

    JobOutcome? Outcome { get; set; }

    Dictionary<string, object> Metadata { get; }

    T GetMetadata<T>()
        where T : class, IJobMetadata;
}

public class JobContext : IJobContext
{
    public Guid JobId { get; set; }

    public Guid TraceId { get; set; }

    public Type? HandlerType { get; set; }

    public JobOutcome? Outcome { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = [];

    public T GetMetadata<T>()
        where T : class, IJobMetadata
    {
        var typed = MetadataFactory.Create<T>(Metadata);
        Metadata = (Dictionary<string, object>)(object)typed;

        return typed;
    }
}
