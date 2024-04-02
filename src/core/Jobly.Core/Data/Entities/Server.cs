namespace Jobly.Core.Data.Entities;

public class Server
{
    public Guid Id { get; set; }

    public DateTime StartedTime { get; set; }
    
    public DateTime LastHeartbeatTime { get; set; }
    
    public int ServiceCount { get; set; }
}