namespace Jobly.Core;

public class JoblyConfiguration
{
    public string DefaultQueue { get; set; } = "default";

    public string? Schema { get; set; } = "jobly";

    /// <summary>
    /// How long completed and deleted jobs are retained before cleanup.
    /// Failed jobs are never auto-expired.
    /// </summary>
    public TimeSpan JobExpirationTimeout { get; set; } = TimeSpan.FromDays(1);
}
