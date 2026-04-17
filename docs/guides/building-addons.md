# Building Addons for Jobly

Jobly's pipeline and metadata system lets you build addons that control job behavior without modifying Jobly's core. This guide explains the architecture using the built-in retry module as a reference implementation.

## Architecture Overview

Jobly provides three extension points for addons:

1. **`IPublishPipelineBehavior<T>`** — runs at publish time, can modify job metadata
2. **`IPipelineBehavior<TRequest, TResponse>`** — wraps handler execution, can catch exceptions and influence failure handling
3. **`IJobContext`** — mutable metadata dictionary available during handler execution, with typed views via `GetMetadata<T>()` and source-generated interfaces

The worker is a generic state machine. On handler failure, it reads `IJobContext.FailureOutcome` and applies whatever state the pipeline decided. If no pipeline set an outcome, the job is marked as `Failed`.

```
Publish time:                         Execution time:
  Publisher                             Worker (workerScope)
    → IPublishPipelineBehavior<T>         creates handlerScope
    → Metadata persisted to DB              → IPipelineBehavior<TReq, TRes>
                                                → Handler
                                                ← Exception
                                            ← Behavior sets FailureOutcome + modifies Metadata
                                          Worker reads FailureOutcome, serializes Metadata, saves
                                          handlerScope disposed (handler's DbContext changes discarded on failure)
```

Metadata is always serialized back — on both success and failure paths. Any changes made to `IJobContext.Metadata` by pipeline behaviors or handlers are persisted to the database.

## Metadata System

### Raw Dictionary Access

`IJobContext.Metadata` is a `Dictionary<string, object>` with native types:

```csharp
// Writing
ctx.Metadata["Priority"] = 5;           // int
ctx.Metadata["CustomerName"] = "John";   // string
ctx.Metadata["Tags"] = new[] { "vip" };  // int[]

// Reading
var priority = (int)(long)ctx.Metadata["Priority"];  // JSON numbers are long
```

The `MetadataSerializer` uses a custom `JsonConverter` that deserializes JSON values as native .NET types (`string`, `long`, `double`, `bool`, `List<object>`, `Dictionary<string, object>`). No `JsonElement` anywhere.

### Typed Metadata via Source Generator

Define an interface extending `IJobMetadata`. The source generator produces an implementation class that extends `Dictionary<string, object>` with typed property accessors:

```csharp
// Define the interface — source generator produces the implementation
public partial interface IOrderMetadata : IJobMetadata
{
    string CustomerName { get; set; }
    int Priority { get; set; }
}

// Handler gets typed metadata via GetMetadata<T>()
public class ProcessOrderHandler(IJobContext ctx) : IJobHandler<ProcessOrder>
{
    public Task HandleAsync(ProcessOrder msg, CancellationToken ct)
    {
        var meta = ctx.GetMetadata<IOrderMetadata>();
        meta.CustomerName = "John";            // typed property → writes to dict
        ctx.Metadata["Priority"];              // raw dict access → same value
    }
}
```

The typed view IS the dictionary — property writes go directly to the underlying `Dictionary<string, object>`. No sync, no flush, no copy.

**Registration:** `IJobContext` is registered in `AddJobly()`. Call `GetMetadata<T>()` to get a typed view. The typed impl replaces the underlying dictionary, so writes flow through directly.

**Nullable properties:** Use `int?` for properties where "not set" must be distinguishable from `0`:

```csharp
public partial interface IMyMetadata : IJobMetadata
{
    int? MaxRetries { get; set; }  // null = not set, 0 = explicitly zero
}
```

## How Retries Are Implemented

The retry module (`AddJoblyRetry`) is built entirely on these primitives. No Jobly core code knows about retries.

### IRetryMetadata

```csharp
public partial interface IRetryMetadata : IJobMetadata
{
    int? MaxRetries { get; set; }
    int RetriedTimes { get; set; }
    int[]? RetryDelays { get; set; }
}
```

### Publish Pipeline: RetryPublishBehavior

At publish time, injects `MaxRetries` and `RetryDelays` into the job's metadata from `RetryOptions`:

```csharp
public class RetryPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        var meta = context.GetMetadata<IRetryMetadata>();

        meta.MaxRetries ??= _options.Value.MaxRetries;

        if (_options.Value.Delays.Length > 0 && meta.RetryDelays == null)
            meta.RetryDelays = _options.Value.Delays;

        return next();
    }
}
```

Uses null-coalescing so per-enqueue metadata set via `Configure<IRetryMetadata>()` takes precedence.

### Handler Pipeline: RetryPipelineBehavior

Wraps handler execution. On failure, reads retry config from typed metadata and decides whether to re-enqueue:

```csharp
public class RetryPipelineBehavior<TRequest, TResponse>(
    IJobContext jobContext,
    IOptions<RetryOptions> options,
    TimeProvider timeProvider) : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        try { return await next(request, ct); }
        catch (Exception) when (request is IJob)
        {
            var meta = jobContext.GetMetadata<IRetryMetadata>();
            var attr = GetRetryAttribute(); // checks [Retry] on handler, then job class
            var maxRetries = meta.MaxRetries ?? attr?.MaxRetries ?? options.Value.MaxRetries;

            if (meta.RetriedTimes < maxRetries)
            {
                meta.RetriedTimes++;
                jobContext.FailureOutcome = new JobFailureOutcome
                {
                    State = State.Enqueued,
                    ScheduleTime = ComputeScheduleTime(meta),
                    ClearHandlerType = true,
                };
            }
            throw;
        }
    }
}
```

Key points:
- `when (request is IJob)` — only runs for persistent jobs, not in-memory requests
- Modifies `meta.RetriedTimes` directly — writes to the underlying dictionary
- Sets `FailureOutcome` — the worker applies state, schedule time, and metadata changes
- Always re-throws — the worker's catch block handles persistence

### What the Worker Does

The worker has zero retry knowledge. On exception:

```csharp
var outcome = jobContext.FailureOutcome;
if (outcome != null)
{
    job.CurrentState = outcome.State;
    if (outcome.ClearHandlerType) job.HandlerType = null;
    if (outcome.ScheduleTime != null) job.ScheduleTime = outcome.ScheduleTime.Value;
    job.Metadata = JsonSerializer.Serialize(jobCtx.Metadata);
}
else
{
    job.CurrentState = State.Failed;
}
```

The worker applies whatever the pipeline decided. It doesn't know if the outcome came from retry, circuit breaking, rate limiting, or any other addon.

### Worker Scope Isolation

The worker and handler use separate DI scopes:
- **Worker scope** — owns Jobly state (Job entity, JobLog, Counter). Only Jobly entities are saved here.
- **Handler scope** — handler + pipeline behaviors get their own DbContext. If the handler throws, the scope is disposed and its change tracker is discarded. No partial handler work leaks into the worker's save.

On success, the worker commits the handler's DbContext (outbox pattern: business entities + published child jobs), then commits Jobly state.

## Building Your Own Addon

### Example: Dead Letter Queue

```csharp
// 1. Typed metadata
public partial interface IDeadLetterMetadata : IJobMetadata
{
    string? DeadLetterQueue { get; set; }
    string? OriginalQueue { get; set; }
}

// 2. Handler behavior — on permanent failure, re-enqueue to dead letter queue
public class DeadLetterBehavior<TRequest, TResponse>(
    IJobContext ctx,
    TimeProvider timeProvider) : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        try { return await next(request, ct); }
        catch (Exception) when (request is IJob && ctx.FailureOutcome == null)
        {
            var meta = ctx.GetMetadata<IDeadLetterMetadata>();
            var dlq = meta.DeadLetterQueue;
            if (dlq != null)
            {
                meta.OriginalQueue = "default";
                ctx.FailureOutcome = new JobFailureOutcome
                {
                    State = State.Enqueued,
                    ScheduleTime = timeProvider.GetUtcNow().UtcDateTime,
                    ClearHandlerType = true,
                };
            }
            throw;
        }
    }
}

// 3. Registration
public static IServiceCollection AddDeadLetterQueue(this IServiceCollection services)
{
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DeadLetterBehavior<,>));
    return services;
}
```

### FailureOutcome Properties

| Property | Type | Purpose |
|----------|------|---------|
| `State` | `State` | Target state (e.g., `Enqueued` for retry/DLQ, `Failed` for permanent failure) |
| `ScheduleTime` | `DateTime?` | When the job becomes eligible for execution again. Null = keep current. |
| `ClearHandlerType` | `bool` | Whether to clear the cached handler type (needed for re-dispatch). |

### Pipeline Ordering

Pipeline behaviors execute as nested middleware (onion model). The last registered behavior is the outermost wrapper:

```csharp
// Dead letter wraps retry — catches permanent failures after retry gives up
services.AddJoblyRetry(o => { o.MaxRetries = 3; o.Delays = [15, 60, 300]; });
services.AddDeadLetterQueue();
```

The dead letter behavior checks `ctx.FailureOutcome == null` before acting, so it only kicks in when retry didn't handle the failure.

### Performance Notes

- **Success path**: Pipeline behaviors add zero overhead. The `try { return await next(...); }` pattern has no allocations. The `catch (Exception) when (request is IJob)` filter is only evaluated when an exception is thrown.
- **Failure path**: Typed metadata property access is a dictionary lookup + type conversion (nanoseconds). The worker serializes metadata back once per failure.
- **No hot path impact**: The worker's fetch-execute-complete cycle is unchanged. Pipeline behaviors run inside `ExecuteJob`, not in the fetch or commit phases.
