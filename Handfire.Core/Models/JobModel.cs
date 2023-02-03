using Handfire.Core.Enums;

namespace Handfire.Core.Models;
public class JobModel
{
    public string Id { get; set; }

    public string Type { get; set; }

    public string Message { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime? ScheduleTime { get; set; }

    public DateTime? ProcessedTime { get; set; }

    public State CurrentState { get; set; }
}
