using Warp.Core.Logging;

namespace Warp.Core.Handlers;

public interface IJobContext
{
    Guid JobId { get; }

    Guid TraceId { get; }

    Type? HandlerType { get; }

    JobOutcome? Outcome { get; set; }

    Dictionary<string, object> Metadata { get; }

    T GetMetadata<T>()
        where T : class, IJobMetadata;

    void ReportProgress(string name, int percent);

    void ReportProgress(int percent);
}

public class JobContext : IJobContext
{
    public Guid JobId { get; set; }

    public Guid TraceId { get; set; }

    public Type? HandlerType { get; set; }

    public JobOutcome? Outcome { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = [];

    internal JobProgressCollector? ProgressCollector { get; set; }

    public T GetMetadata<T>()
        where T : class, IJobMetadata
    {
        var typed = MetadataFactory.Create<T>(Metadata);
        Metadata = (Dictionary<string, object>)(object)typed;

        return typed;
    }

    public void ReportProgress(int percent) => ReportProgress(string.Empty, percent);

    public void ReportProgress(string name, int percent)
    {
        var clamped = percent;
        if (clamped < 0)
        {
            clamped = 0;
        }
        else if (clamped > 100)
        {
            clamped = 100;
        }

        ProgressCollector?.Report(name ?? string.Empty, clamped);
    }
}
