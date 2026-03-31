namespace Jobly.Core.Models;

public class DashboardStatistics
{
    public int Total { get; set; }

    public int Pending { get; set; }

    public int Scheduled { get; set; }

    public int Created { get; set; }

    public int Failed { get; set; }

    public int Completed { get; set; }

    public int Processing { get; set; }

    public int Servers { get; set; }

    public int Awaiting { get; set; }

    public int Deleted { get; set; }

    public int Messages { get; set; }

    public long TotalSucceeded { get; set; }

    public long TotalFailed { get; set; }

    public long TotalDeleted { get; set; }

    public long TotalCreated { get; set; }

    public int Batches { get; set; }

    public int BatchesActive { get; set; }

    public int BatchesCompleted { get; set; }

    public int BatchesFailed { get; set; }

    public int MessagesEnqueued { get; set; }

    public int MessagesProcessing { get; set; }

    public int MessagesCompleted { get; set; }

    public int MessagesFailed { get; set; }

    public string? DatabaseConnection { get; set; }
}
