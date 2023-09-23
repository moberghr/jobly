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
}
