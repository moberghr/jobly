namespace Jobly.Core;

public class JoblyConfiguration
{
    public int RetryCount { get; set; }

    public string DefaultQueue { get; set; } = "default";
}
