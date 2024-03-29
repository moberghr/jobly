namespace Jobly.Core;

public class JoblyConfiguration
{
    public int RetryCount { get; set; }
}

public class JoblyWorkerConfiguration
{
    public int WorkerCount { get; set; } = 10;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
}

