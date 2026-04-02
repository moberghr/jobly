using Jobly.Core.Enums;

namespace Jobly.Core.Models;

public class JobModel
{
    public Guid Id { get; set; }

    public string? Type { get; set; }

    public string? Message { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime? ScheduleTime { get; set; }

    public DateTime? ProcessedTime { get; set; }

    public State CurrentState { get; set; }

    public CancellationMode CancellationMode { get; set; }

    public string? HandlerType { get; set; }
}
