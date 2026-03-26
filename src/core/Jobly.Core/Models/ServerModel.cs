namespace Jobly.Core.Models;

public class ServerModel
{
    public Guid Id { get; set; }

    public string ServerName { get; set; } = string.Empty;

    public DateTime StartedTime { get; set; }

    public DateTime LastHeartbeatTime { get; set; }

    public int ServiceCount { get; set; }

    public List<WorkerModel> Workers { get; set; } = new();
}

public class WorkerModel
{
    public Guid WorkerId { get; set; }

    public DateTime StartedTime { get; set; }

    public DateTime? LastHeartbeatTime { get; set; }

    public Guid? CurrentJobId { get; set; }

    public string? CurrentJobType { get; set; }
}
