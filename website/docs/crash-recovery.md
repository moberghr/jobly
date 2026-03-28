---
sidebar_position: 3
---

# Crash Recovery

Jobly uses a **sliding invisibility timeout** (inspired by Hangfire) to detect and recover from worker/server crashes.

## How It Works

1. When a worker picks up a job, it sets `LastKeepAlive = now` on the job row.
2. During execution, a background loop refreshes `LastKeepAlive` every minute.
3. If the worker crashes, the keep-alive stops. After 5 minutes, the job's `LastKeepAlive` becomes stale.
4. The health manager detects stale jobs and requeues them automatically.

## Key Properties

- **Per-job detection**: Unlike server-level heartbeats, this detects individual worker crashes within a live server.
- **No lost retries**: Crash requeues do NOT count against `MaxRetries`. The job didn't fail — the server died.
- **Long-running jobs are safe**: The keep-alive refreshes continuously, so a job running for hours won't be falsely requeued.
- **Concurrent safety**: Row locking prevents multiple health managers from double-requeuing the same job.

## Configuration

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    // How long before a stale job is requeued (default: 5 minutes)
    options.InvisibilityTimeout = TimeSpan.FromMinutes(5);

    // Server heartbeat timeout (default: 5 minutes)
    options.HealthCheckTimeout = TimeSpan.FromMinutes(5);

    // How often health checks run (default: 10 seconds)
    options.HealthCheckInterval = TimeSpan.FromSeconds(10);
});
```

## Timeline Example

```
T=0:00  Worker picks up Job, sets LastKeepAlive
T=1:00  Keep-alive refreshes LastKeepAlive
T=1:30  Server crashes. Keep-alive stops.
T=5:00  Health manager: job has no keep-alive for >5 min
        → Job requeued (State = Enqueued, RetriedTimes unchanged)
        → Another worker picks it up and completes it
```
