using Jobly.Core.Enums;

namespace Jobly.Core.Models;

public class MessageModel
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string Queue { get; set; } = "default";

    public State CurrentState { get; set; }

    public int JobCount { get; set; }

    public DateTime CreateTime { get; set; }
}

public class MessageDetailModel : MessageModel
{
    public List<JobModel> Jobs { get; set; } = new();
}
