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

    public int? RecurringJobId { get; set; }

    public State? State { get; set; }
}
