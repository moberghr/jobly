---
sidebar_position: 1
---

# Configuration

## Core Configuration (`JoblyConfiguration`)

Used by the publisher side (`AddJobly<TContext>`):

```csharp
builder.Services.AddJobly<AppDbContext>(options =>
{
    options.Schema = "jobly";      // Database schema for all Jobly tables (default: "jobly", null for default schema)
    options.DefaultQueue = "default"; // Queue name when none specified (default: "default")
    options.JobExpirationTimeout = TimeSpan.FromDays(1); // How long completed/deleted jobs kept (default: 1 day)
});
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Schema` | `string?` | `"jobly"` | Database schema for all Jobly tables. Set to `null` for the database's default schema. |
| `DefaultQueue` | `string` | `"default"` | Queue used when no queue is specified at publish time |
| `JobExpirationTimeout` | `TimeSpan` | `1 day` | How long completed/deleted jobs are kept before cleanup. Failed jobs never expire. |

### Naming Conventions

Jobly's entity configurations do **not** hardcode table or column names. If you use a naming convention plugin (e.g., `UseSnakeCaseNamingConvention()`), it will transform Jobly's tables and columns just like your own entities:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());
```

This produces tables like `jobly.job`, `jobly.job_log`, `jobly.server`, etc.

## Retry Configuration

Configure retry behavior with `AddJoblyRetry()`:

```csharp
services.AddJoblyRetry(options =>
{
    options.MaxRetries = 3;               // Default max retries (default: 0)
    options.Delays = [15, 60, 300];       // Retry delays in seconds (default: [15, 60, 300])
    options.JitterFactor = 0.2;           // Random ±20% jitter on each delay (default: 0, no jitter)
});
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxRetries` | `int` | `0` | Default max retries when no `[Retry]` attribute is present |
| `Delays` | `int[]` | `[15, 60, 300]` | Delay in seconds between retries. Last value is reused if fewer delays than retries |
| `JitterFactor` | `double` | `0.0` | Multiplicative jitter applied to each delay: `delay * (1 + JitterFactor * rand(-1, 1))`. Clamped to `[0, 1]`. Global only — no per-job override. Helps avoid retry thundering herds |

Per-job override via `[Retry]` attribute on handler or job class, or per-enqueue via metadata. See [Jobs](/docs/patterns/jobs#retries).

## Mutex Configuration

Enable mutex (concurrency control) with `AddJoblyMutex()`:

```csharp
services.AddJoblyMutex();
```

No options — just register and use `.WithMutex("key")` or `[Mutex("key")]` at publish time. See [Mutex](/docs/features/mutex) for details.

## Worker Configuration (`JoblyWorkerConfiguration`)

Extends `JoblyConfiguration`. Used by the worker side (`AddJoblyWorker<TContext>`):

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    // Worker
    options.WorkerCount = 10;
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.Queues = ["a-critical", "b-default", "c-low"];

    // Dispatcher mode (batch-fetch instead of per-worker polling)
    options.UseDispatcher = false;

    // Cancellation
    options.CancellationCheckInterval = TimeSpan.FromSeconds(5);

    // Server identity
    options.ServerName = "my-api-server";
    options.ServerId = Guid.NewGuid(); // Auto-generated, rarely needs override

    // Health & crash recovery
    options.HealthCheckInterval = TimeSpan.FromSeconds(10);
    options.HealthCheckTimeout = TimeSpan.FromMinutes(5);
    options.InvisibilityTimeout = TimeSpan.FromMinutes(5);

    // Job retention
    options.JobExpirationTimeout = TimeSpan.FromDays(1);
    options.ExpirationBatchSize = 1000;
    options.MaxExpirableJobCount = null; // Null = unlimited

    // Background task intervals
    options.OrchestrationInterval = TimeSpan.FromSeconds(10);
    options.MessageRoutingInterval = TimeSpan.FromSeconds(1);
    options.CounterAggregationInterval = TimeSpan.FromSeconds(5);
    options.ServerCleanupInterval = TimeSpan.FromSeconds(30);
    options.StaleJobRecoveryInterval = TimeSpan.FromSeconds(30);
    options.ExpirationCleanupInterval = TimeSpan.FromSeconds(60);

    // Inherited from JoblyConfiguration
    options.DefaultQueue = "default";
});
```

### Worker

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `WorkerCount` | `int` | `min(CPU * 5, 20)` | Number of concurrent worker threads |
| `PollingInterval` | `TimeSpan` | `1 second` | Delay between polls when no jobs are available |
| `Queues` | `string[]` | `["default"]` | Queues this worker subscribes to. Processed in alphabetical order |

### Handler Logging

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnableHandlerLogging` | `bool` | `true` | When true, handler `ILogger` output is captured and stored in the JobLog table. Set to `false` to suppress handler log capture (lifecycle events like Created/Completed are always recorded). |

### Cancellation

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `CancellationCheckInterval` | `TimeSpan` | `5 seconds` | How often the worker checks if a running job has been cancelled. Also refreshes the keep-alive timestamp. |

### Dispatcher Mode

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `UseDispatcher` | `bool` | `false` | When true, uses a batch-fetch dispatcher instead of per-worker polling |

By default, each worker polls the database independently for the next job. With `UseDispatcher = true`, a single dispatcher thread batch-fetches jobs and distributes them to workers via an in-memory channel. This reduces database load when running many workers, at the cost of slightly higher latency for the first job in a batch.

Use dispatcher mode when you have many workers (20+) and want to reduce database polling pressure. For most setups, the default per-worker polling is simpler and works well.

### Worker Groups

By default, all workers share the same queues and polling interval. Use worker groups for fine-grained control:

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    // Top-level settings become the first worker group
    options.WorkerCount = 5;
    options.Queues = ["critical"];
    options.PollingInterval = TimeSpan.FromMilliseconds(100);

    // Additional groups
    options.AddWorkerGroup(group =>
    {
        group.WorkerCount = 2;
        group.Queues = ["reports", "default"];
        group.PollingInterval = TimeSpan.FromSeconds(5);
    });
});
```

This creates 7 workers total: 5 polling `critical` every 100ms, and 2 polling `reports`/`default` every 5s. All workers share the same server identity and health monitoring.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `WorkerCount` | `int` | `min(CPU * 5, 20)` | Number of workers in this group |
| `Queues` | `string[]` | `["default"]` | Queues this group subscribes to |
| `PollingInterval` | `TimeSpan` | `1 second` | Delay between polls for this group |

### Server Identity

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ServerName` | `string?` | `null` (uses `MachineName.ServerId`) | Display name shown in the dashboard |
| `ServerId` | `Guid` | Auto-generated | Unique server ID. Override only if you need deterministic IDs |

### Health & Crash Recovery

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `HealthCheckInterval` | `TimeSpan` | `10 seconds` | How often the health manager runs (heartbeat, stale job detection, cleanup) |
| `HealthCheckTimeout` | `TimeSpan` | `5 minutes` | Time without heartbeat before a server is considered dead and removed |
| `InvisibilityTimeout` | `TimeSpan` | `5 minutes` | Time without keep-alive before a processing job is considered stale and requeued. Workers refresh keep-alive every `InvisibilityTimeout / 5` |

### Job Retention

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `JobExpirationTimeout` | `TimeSpan` | `1 day` | How long completed/deleted jobs are kept before cleanup (inherited from `JoblyConfiguration`) |
| `ExpirationBatchSize` | `int` | `1000` | Batch size for cleanup operations |
| `MaxExpirableJobCount` | `int?` | `null` | Max jobs with `ExpireAt` to retain. Oldest deleted first. `null` = disabled (no cap). |

:::info Failed jobs never expire
Failed jobs have `ExpireAt = null` and are never automatically deleted. They must be manually deleted or requeued from the dashboard.
:::

### Background Task Intervals

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `OrchestrationInterval` | `TimeSpan` | `10 seconds` | Fallback sweep interval for parent finalization |
| `MessageRoutingInterval` | `TimeSpan` | `1 second` | Message routing poll interval |
| `CounterAggregationInterval` | `TimeSpan` | `5 seconds` | Counter aggregation interval |
| `ServerCleanupInterval` | `TimeSpan` | `30 seconds` | Dead server cleanup interval |
| `StaleJobRecoveryInterval` | `TimeSpan` | `30 seconds` | Stale job recovery interval |
| `ExpirationCleanupInterval` | `TimeSpan` | `60 seconds` | Expiration cleanup interval |

## Queue Ordering

Queues are processed in **alphabetical order**. Use prefixes to control priority:

```csharp
options.Queues = ["a-critical", "b-default", "c-low"];
```

A worker always picks up jobs from `a-critical` before `b-default`, and `b-default` before `c-low`. Within a queue, jobs are ordered by schedule time.
