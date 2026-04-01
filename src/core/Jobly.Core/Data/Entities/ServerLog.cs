namespace Jobly.Core.Data.Entities;

public class ServerLog
{
    public int Id { get; set; }

    public Guid ServerId { get; set; }

    public int? ServerTaskId { get; set; }

    public ServerTask? ServerTask { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    public DateTime Timestamp { get; set; }

    public double? DurationMs { get; set; }
}
