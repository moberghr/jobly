---
sidebar_position: 6
---

# Configuration

## Core Configuration (`JoblyConfiguration`)

Used by the publisher side (`AddJobly<TContext>`):

```csharp
builder.Services.AddJobly<AppDbContext>(options =>
{
    options.RetryCount = 3;        // Default max retries for new jobs (default: 0)
    options.DefaultQueue = "default"; // Queue name when none specified (default: "default")
});
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `RetryCount` | `int` | `0` | Default max retries for jobs that don't specify their own |
| `DefaultQueue` | `string` | `"default"` | Queue used when no queue is specified at publish time |

## Worker Configuration (`JoblyWorkerConfiguration`)

Extends `JoblyConfiguration`. Used by the worker side (`AddJoblyWorker<TContext>`):

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    // Worker
    options.WorkerCount = 10;
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.Queues = new[] { "a-critical", "b-default", "c-low" };

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

    // Inherited from JoblyConfiguration
    options.RetryCount = 3;
    options.DefaultQueue = "default";
});
```

### Worker

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `WorkerCount` | `int` | `10` | Number of concurrent worker threads |
| `PollingInterval` | `TimeSpan` | `1 second` | Delay between polls when no jobs are available |
| `Queues` | `string[]` | `["default"]` | Queues this worker subscribes to. Processed in alphabetical order |

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
| `JobExpirationTimeout` | `TimeSpan` | `1 day` | How long completed/deleted jobs are kept before cleanup |
| `ExpirationBatchSize` | `int` | `1000` | Max number of expired jobs to clean up per health check cycle |

:::info Failed jobs never expire
Failed jobs have `ExpireAt = null` and are never automatically deleted. They must be manually deleted or requeued from the dashboard.
:::

## Queue Ordering

Queues are processed in **alphabetical order**. Use prefixes to control priority:

```csharp
options.Queues = new[] { "a-critical", "b-default", "c-low" };
```

A worker always picks up jobs from `a-critical` before `b-default`, and `b-default` before `c-low`. Within a queue, jobs are ordered by schedule time.
