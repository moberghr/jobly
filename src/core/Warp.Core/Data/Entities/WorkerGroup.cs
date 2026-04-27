namespace Warp.Core.Data.Entities;

public class WorkerGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ServerId { get; set; }

    public Server? Server { get; set; }

    public int WorkerCount { get; set; }

    public string Queues { get; set; } = "default";

    public double PollingIntervalMs { get; set; } = 1000;

    public DateTime? PausedAt { get; set; }
}
