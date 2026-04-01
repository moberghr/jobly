namespace Jobly.Core.Data.Entities;

public class RecurringJobLog
{
    public int Id { get; set; }

    public int RecurringJobId { get; set; }

    public Guid JobId { get; set; }

    public DateTime CreatedAt { get; set; }
}
