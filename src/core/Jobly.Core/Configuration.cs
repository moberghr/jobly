namespace Jobly.Core;

public class JoblyConfiguration
{
    public int RetryCount { get; set; }

    public string DefaultQueue { get; set; } = "default";

    /// <summary>
    /// How long completed and deleted jobs are retained before cleanup.
    /// Failed jobs are never auto-expired.
    /// </summary>
    public TimeSpan JobExpirationTimeout { get; set; } = TimeSpan.FromDays(1);
}
