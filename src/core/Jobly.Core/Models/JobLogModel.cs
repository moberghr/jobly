namespace Jobly.Core.Models;

public class JobLogModel
{
    public Guid Id { get; set; }

    public string EventType { get; set; } = "Log";

    public DateTime Timestamp { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Exception { get; set; }

    public double? DurationMs { get; set; }

    public Guid? WorkerId { get; set; }
}
