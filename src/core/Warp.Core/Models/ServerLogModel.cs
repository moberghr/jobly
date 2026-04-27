namespace Warp.Core.Models;

public class ServerTaskSummary
{
    public string TaskName { get; set; } = string.Empty;

    public string? LastStatus { get; set; }

    public string? LastMessage { get; set; }

    public DateTime? LastRun { get; set; }

    public double? LastDurationMs { get; set; }

    public double? IntervalSeconds { get; set; }
}

public class ServerLogModel
{
    public int Id { get; set; }

    public string TaskName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    public DateTime Timestamp { get; set; }

    public double? DurationMs { get; set; }
}
