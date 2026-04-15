# Building Addons for Jobly

Jobly's pipeline and metadata system lets you build addons that control job behavior without modifying Jobly's core. This guide explains the architecture using the built-in retry module as a reference implementation.

## Architecture Overview

Jobly provides three extension points for addons:

1. **`IPublishPipelineBehavior<T>`** — runs at publish time, can modify job metadata
2. **`IPipelineBehavior<TRequest, TResponse>`** — wraps handler execution, can catch exceptions and influence failure handling
3. **`IJobContext.Metadata`** — mutable dictionary available during handler execution, persisted back to the database on failure

The worker is a generic state machine. On handler failure, it reads `IJobContext.FailureOutcome` and applies whatever state the pipeline decided. If no pipeline set an outcome, the job is marked as `Failed`.

```
Publish time:                         Execution time:
  Publisher                             Worker
    → IPublishPipelineBehavior<T>         → IPipelineBehavior<TReq, TRes>
    → Metadata persisted to DB                → Handler
                                              ← Exception
                                          ← Behavior sets FailureOutcome + modifies Metadata
                                        Worker reads FailureOutcome, serializes Metadata, saves
```

## How Retries Are Implemented

The retry module (`AddJoblyRetry`) is built entirely on these primitives. No Jobly core code knows about retries.

### Publish Pipeline: RetryPublishBehavior

At publish time, injects `$maxRetries` and `$retryDelays` into the job's metadata from `RetryOptions`:

```csharp
public class RetryPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    private readonly IOptions<RetryOptions> _options;

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        if (!context.Metadata.ContainsKey("$maxRetries"))
        {
            context.Metadata["$maxRetries"] = _options.Value.MaxRetries.ToString();
        }

        if (_options.Value.Delays.Length > 0 && !context.Metadata.ContainsKey("$retryDelays"))
        {
            context.Metadata["$retryDelays"] = JsonSerializer.Serialize(_options.Value.Delays);
        }

        return next();
    }
}
```

Uses `ContainsKey` so user-registered `IPublishPipelineBehavior<T>` can override per job type.

### Handler Pipeline: RetryPipelineBehavior

Wraps handler execution. On failure, reads retry config from metadata and decides whether to re-enqueue:

```csharp
public class RetryPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(request, cancellationToken);
        }
        catch (Exception) when (request is IJob)
        {
            var metadata = _jobContext.Metadata;
            var maxRetries = TryGetInt(metadata, "$maxRetries") ?? _options.Value.MaxRetries;
            var retriedTimes = TryGetInt(metadata, "$retriedTimes") ?? 0;

            if (retriedTimes < maxRetries)
            {
                // Write updated count directly to metadata
                _jobContext.Metadata["$retriedTimes"] = (retriedTimes + 1).ToString();

                // Tell the worker to re-enqueue with a delay
                _jobContext.FailureOutcome = new JobFailureOutcome
                {
                    State = State.Enqueued,
                    ScheduleTime = ComputeScheduleTime(retriedTimes),
                    ClearHandlerType = true,
                };
            }

            throw; // always re-throw — worker reads FailureOutcome
        }
    }
}
```

Key points:
- `when (request is IJob)` — only runs for persistent jobs, not in-memory requests
- Writes `$retriedTimes` directly to `_jobContext.Metadata` — the worker serializes it back
- Sets `FailureOutcome` — the worker applies state, schedule time, and metadata changes
- Always re-throws — the worker's catch block handles persistence

### What the Worker Does

The worker has zero retry knowledge. On exception:

```csharp
catch (Exception e)
{
    var jobCtx = scope.ServiceProvider.GetRequiredService<JobContext>();
    var outcome = jobCtx.FailureOutcome;

    if (outcome != null)
    {
        job.CurrentState = outcome.State;
        if (outcome.ClearHandlerType) job.HandlerType = null;
        if (outcome.ScheduleTime != null) job.ScheduleTime = outcome.ScheduleTime.Value;
        job.Metadata = JsonSerializer.Serialize(jobCtx.Metadata); // persist metadata changes
    }
    else
    {
        job.CurrentState = State.Failed;
    }

    FinalizeJobState(context, job, e, durationMs); // counters, logs, cleanup
}
```

The worker applies whatever the pipeline decided. It doesn't know if the outcome came from retry, circuit breaking, rate limiting, or any other addon.

Metadata is always serialized back — on both success and failure paths. Any changes made to `IJobContext.Metadata` by pipeline behaviors or handlers are persisted to the database.

## Building Your Own Addon

### Example: Dead Letter Queue

An addon that moves permanently failed jobs to a dead letter queue instead of marking them as Failed:

```csharp
// 1. Options
public class DeadLetterOptions
{
    public string Queue { get; set; } = "dead-letter";
}

// 2. Publish behavior — tag jobs for dead letter processing
public class DeadLetterPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        context.Metadata["$deadLetterQueue"] = _options.Value.Queue;
        return next();
    }
}

// 3. Handler behavior — on permanent failure, re-enqueue to dead letter queue
public class DeadLetterBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken)
    {
        try { return await next(request, cancellationToken); }
        catch (Exception) when (request is IJob && _jobContext.FailureOutcome == null)
        {
            // Only act if no other behavior (e.g. retry) already handled it
            var dlq = _jobContext.Metadata.GetValueOrDefault("$deadLetterQueue");
            if (dlq != null)
            {
                _jobContext.FailureOutcome = new JobFailureOutcome
                {
                    State = State.Enqueued,
                    ScheduleTime = _timeProvider.GetUtcNow().UtcDateTime,
                    ClearHandlerType = true,
                };
                _jobContext.Metadata["$originalQueue"] = _jobContext.Metadata.GetValueOrDefault("queue", "default");
                // Worker will serialize this metadata back and re-enqueue
            }
            throw;
        }
    }
}

// 4. Registration
public static IServiceCollection AddDeadLetterQueue(this IServiceCollection services,
    Action<DeadLetterOptions>? configure = null)
{
    if (configure != null) services.Configure(configure);
    else services.AddOptions<DeadLetterOptions>();

    services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(DeadLetterPublishBehavior<>));
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DeadLetterBehavior<,>));
    return services;
}
```

### Metadata Conventions

| Convention | Meaning |
|-----------|---------|
| `$` prefix | Reserved for infrastructure/addon metadata. Not inherited by child jobs. |
| `ContainsKey` check | Publish behaviors use `ContainsKey` before setting — lets per-type behaviors override. |
| `FailureOutcome == null` check | Handler behaviors check if another behavior already set an outcome before overriding. |

### FailureOutcome Properties

| Property | Type | Purpose |
|----------|------|---------|
| `State` | `State` | Target state for the job (e.g., `Enqueued` for retry, `Failed` for permanent failure) |
| `ScheduleTime` | `DateTime?` | When the job should become eligible for execution again. Null = keep current. |
| `ClearHandlerType` | `bool` | Whether to clear the cached handler type (needed for re-dispatch). |

### Pipeline Ordering

Pipeline behaviors execute as nested middleware (onion model). The last registered behavior is the outermost wrapper. For addons that compose (e.g., retry + dead letter):

```csharp
// Dead letter wraps retry — catches permanent failures after retry gives up
services.AddJoblyRetry(o => { o.MaxRetries = 3; o.Delays = [15, 60, 300]; });
services.AddDeadLetterQueue(o => { o.Queue = "dead-letter"; });
```

The dead letter behavior checks `_jobContext.FailureOutcome == null` before acting, so it only kicks in when retry didn't handle the failure.

### Performance Notes

- **Success path**: Pipeline behaviors add zero overhead. The `try { return await next(...); }` pattern has no allocations. The `catch (Exception) when (request is IJob)` filter is only evaluated when an exception is thrown.
- **Failure path**: Metadata reads are dictionary lookups (nanoseconds). JSON deserialization of `$retryDelays` only happens if the key exists. The worker serializes metadata back once per failure.
- **No hot path impact**: The worker's fetch-execute-complete cycle is unchanged. Pipeline behaviors run inside `ExecuteJob`, not in the fetch or commit phases.
