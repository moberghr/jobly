namespace Jobly.Worker;

public class JoblyWorkerConfiguration
{
    public int WorkerCount { get; set; } = 10;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    public IWakeupProvider WakeupProvider { get; set; }
    
}
