using Jobly.Core.Enums;

namespace Jobly.Core.Data.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string Queue { get; set; } = "default";

    public DateTime CreateTime { get; set; }

    public State CurrentState { get; set; }

    public int JobCount { get; set; }
}
