using Jobly.Core;

namespace Jobly.Worker;

public class JoblyWorkerConfiguration : JoblyConfiguration
{
    
    /// <summary>
    /// How many worker instances should be created.
    /// </summary>
    public int WorkerCount { get; set; } = 10;

    /// <summary>
    /// Each time the worker polls for a job, it will wait for this interval before polling again.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    
    
    /// <summary>
    /// Worker Id should be unique for each worker. If you need to control the worker id, you can set it here.
    /// </summary>
    public Guid WorkerId = Guid.NewGuid();
}