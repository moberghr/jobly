namespace Jobly.Core.Data.Entities;

public class Worker
{
    public Guid Id { get; set; }

    public required Guid ServerId { get; set; }

    public Server? Server { get; set; }

    public required DateTime StartedTime { get; set; }

    public DateTime? LastHeartbeatTime { get; set; }

    public Guid? WorkerGroupId { get; set; }

    public WorkerGroup? WorkerGroup { get; set; }
}
