namespace Warp.Core.Models;

public class WorkerJobLogModel
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public string? JobType { get; set; }

    public string EventType { get; set; } = "Log";

    public DateTime Timestamp { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Exception { get; set; }

    public double? DurationMs { get; set; }
}
