namespace Jobly.Core.Data.Entities;

public class Server
{
    public required Guid Id { get; set; }

    public string ServerName { get; set; } = Environment.MachineName;

    public required DateTime StartedTime { get; set; }

    public required DateTime LastHeartbeatTime { get; set; }

    public int ServiceCount { get; set; }
}
