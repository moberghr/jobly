using Jobly.Core;

namespace Jobly.Worker;

public class WorkerGroupConfiguration
{
    public int WorkerCount { get; set; } = Math.Min(Environment.ProcessorCount * 5, 20);

    public string[] Queues { get; set; } = ["default"];

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
}

public class JoblyWorkerConfiguration : JoblyConfiguration
{
    private static readonly int DefaultWorkerCount = Math.Min(Environment.ProcessorCount * 5, 20);

    /// <summary>
    /// How many worker instances should be created. Applies to the implicit default worker group.
    /// </summary>
    public int WorkerCount { get; set; } = DefaultWorkerCount;

    /// <summary>
    /// Each time the worker polls for a job, it will wait for this interval before polling again.
    /// Applies to the implicit default worker group.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Queues this worker subscribes to. Applies to the implicit default worker group.
    /// </summary>
    public string[] Queues { get; set; } = ["default"];

    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan CounterAggregationInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan ServerCleanupInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan StaleJobRecoveryInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ExpirationCleanupInterval { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan OrchestrationInterval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan MessageRoutingInterval { get; set; } = TimeSpan.FromSeconds(1);

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

    public TimeSpan JobExpirationTimeout { get; set; } = TimeSpan.FromDays(1);

    public int ExpirationBatchSize { get; set; } = 1000;

    /// <summary>
    /// When true, uses a single dispatcher per worker group that batch-fetches jobs
    /// and distributes them to workers, reducing per-job DB overhead.
    /// When false (default), each worker independently fetches its own jobs.
    /// </summary>
    public bool UseDispatcher { get; set; }

    internal List<WorkerGroupConfiguration> ExplicitWorkerGroups { get; } = [];

    /// <summary>
    /// Adds a worker group with its own worker count, queues, and polling interval.
    /// Top-level WorkerCount/Queues/PollingInterval become an additional implicit group.
    /// </summary>
    public void AddWorkerGroup(Action<WorkerGroupConfiguration> configure)
    {
        var group = new WorkerGroupConfiguration();
        configure(group);
        ExplicitWorkerGroups.Add(group);
    }

    /// <summary>
    /// Returns all effective worker groups. Top-level settings always form the first group.
    /// Any groups added via <see cref="AddWorkerGroup"/> are appended after.
    /// </summary>
    internal List<WorkerGroupConfiguration> GetEffectiveWorkerGroups()
    {
        var groups = new List<WorkerGroupConfiguration>
        {
            new()
            {
                WorkerCount = WorkerCount,
                Queues = Queues,
                PollingInterval = PollingInterval,
            },
        };

        groups.AddRange(ExplicitWorkerGroups);
        return groups;
    }

    /// <summary>
    /// Total number of workers across all groups.
    /// </summary>
    internal int TotalWorkerCount => GetEffectiveWorkerGroups().Sum(g => g.WorkerCount);
}
