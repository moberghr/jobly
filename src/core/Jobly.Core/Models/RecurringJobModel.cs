namespace Jobly.Core.Models;

public class RecurringJobModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Cron { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public DateTime? NextExecution { get; set; }

    public DateTime? LastExecution { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class RecurringJobDetailModel : RecurringJobModel
{
    public string? Message { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? NextJobId { get; set; }

    public Guid? LastJobId { get; set; }

    public int TotalJobCount { get; set; }
}
