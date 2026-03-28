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

    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a job can go without a keep-alive refresh before being considered stale and requeued.
    /// Workers refresh keep-alive every InvisibilityTimeout / 5 during execution.
    /// </summary>
    public TimeSpan InvisibilityTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Worker Id should be unique for each worker. If you need to control the worker id, you can set it here.
    /// </summary>
    public Guid ServerId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name for this server in the dashboard. Defaults to MachineName.
    /// </summary>
    public string? ServerName { get; set; }

    public string[] Queues { get; set; } = ["default"];

    public TimeSpan JobExpirationTimeout { get; set; } = TimeSpan.FromDays(1);

    public int ExpirationBatchSize { get; set; } = 1000;
}
