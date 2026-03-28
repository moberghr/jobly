namespace Jobly.Core.Logging;

public class JobExecutionInfo
{
    public Guid JobId { get; set; }

    public Guid TraceId { get; set; }
}

public static class JobExecutionContext
{
    private static readonly AsyncLocal<JobExecutionInfo?> _current = new();

    public static JobExecutionInfo? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
