namespace Jobly.Core.Models;

public class WorkerDetailModel
{
    public Guid WorkerId { get; set; }

    public DateTime StartedTime { get; set; }

    public DateTime? LastHeartbeatTime { get; set; }

    public Guid? CurrentJobId { get; set; }

    public string? CurrentJobType { get; set; }

    public Guid ServerId { get; set; }

    public string ServerName { get; set; } = string.Empty;

    public string? Queues { get; set; }

    public double? PollingIntervalMs { get; set; }

    public DateTime? ServerPausedAt { get; set; }

    public Guid? WorkerGroupId { get; set; }

    public DateTime? WorkerGroupPausedAt { get; set; }
}
