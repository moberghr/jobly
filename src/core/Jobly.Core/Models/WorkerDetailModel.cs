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
}
