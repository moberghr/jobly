using Jobly.Core.Enums;

namespace Jobly.Core.Helper;

public class JobParameters
{
    internal string? Message { get; set; }

    internal string? Type { get; set; }

    public int Retries { get; set; }

    public DateTime? ScheduleTime { get; set; }

    public int? MaxRetries { get; set; }

    public string? Queue { get; set; }

    public Guid? ParentId { get; set; }

    public State? State { get; set; }

    public string? Mutex { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }
}
