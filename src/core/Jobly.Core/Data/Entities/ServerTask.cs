namespace Jobly.Core.Data.Entities;

public class ServerTask
{
    public int Id { get; set; }

    public Guid ServerId { get; set; }

    public string TaskName { get; set; } = string.Empty;

    /// <summary>
    /// Interval in seconds between runs. Null means the task is disabled.
    /// </summary>
    public double? IntervalSeconds { get; set; }

    public string? LastStatus { get; set; }

    public string? LastMessage { get; set; }

    public DateTime? LastRun { get; set; }

    public double? LastDurationMs { get; set; }
}
