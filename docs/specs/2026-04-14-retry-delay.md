# Spec: Retry as External Pipeline Module

## Problem

1. Retry logic is hardcoded in the worker. Not configurable, composable, or extensible.
2. When a job fails with retries, `ScheduleTime` isn't updated — same worker retries immediately.

## Solution

Retries become an opt-in module built on generic Jobly primitives. Jobly Core has zero retry knowledge.

### Jobly Core: Generic Failure Outcome

Core provides a generic mechanism for pipeline behaviors to influence failure handling:

```csharp
public class JobFailureOutcome
{
    public State State { get; init; }
    public DateTime? ScheduleTime { get; init; }
    public int RetriedTimesIncrement { get; init; }
    public bool ClearHandlerType { get; init; }
}

// Added to existing IJobContext
public interface IJobContext : IJobMetadata
{
    Guid JobId { get; }
    Guid TraceId { get; }
    int RetriedTimes { get; }                          // NEW
    JobFailureOutcome? FailureOutcome { get; set; }    // NEW
}
```

Worker reads `FailureOutcome` after handler failure and applies it mechanically. No retry concepts.

### Retry Module: Opt-in via `AddJoblyRetry`

```csharp
services.AddJoblyWorker<AppDbContext>();
services.AddJoblyRetry(config =>
{
    config.MaxRetries = 3;
    config.Delays = [15, 60, 300];
});
```

The module provides:
- **`RetryOptions`** — `MaxRetries`, `Delays` (int[] of seconds, last reused)
- **`RetryPublishBehavior<T>`** — `IPublishPipelineBehavior<T>` that injects `$maxRetries` and `$retryDelays` into metadata from `RetryOptions`
- **`RetryPipelineBehavior<TReq, TRes>`** — `IPipelineBehavior` that catches handler failures, reads metadata, sets `FailureOutcome`
- **`AddJoblyRetry()`** — extension method registering both behaviors + options

### Metadata Convention

| Key | Type | Set by |
|-----|------|--------|
| `$maxRetries` | string (int) | RetryPublishBehavior or user's publish pipeline |
| `$retryDelays` | string (JSON int[]) | RetryPublishBehavior or user's publish pipeline |

`$`-prefixed keys are not inherited by child jobs (Publisher skips them in `RunPublishPipeline`).

### User Customization

**Per job type (override global config):**
```csharp
public class OrderRetryPolicy : IPublishPipelineBehavior<ProcessOrder>
{
    public Task HandleAsync(PublishContext<ProcessOrder> ctx,
        PublishPipelineDelegate next, CancellationToken ct)
    {
        ctx.Metadata["$maxRetries"] = "5";
        ctx.Metadata["$retryDelays"] = "[10, 30, 60]";
        return next();
    }
}
```

**Custom failure handling (replace retry behavior entirely):**
```csharp
public class MyFailureHandler<TReq, TRes> : IPipelineBehavior<TReq, TRes>
    where TReq : IRequest<TRes>
{
    private readonly IJobContext _ctx;
    public async Task<TRes> HandleAsync(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        try { return await next(); }
        catch (Exception) when (req is IJob)
        {
            _ctx.FailureOutcome = new JobFailureOutcome
            {
                State = State.Enqueued,
                ScheduleTime = DateTime.UtcNow.AddMinutes(5),
                RetriedTimesIncrement = 1,
                ClearHandlerType = true,
            };
            throw;
        }
    }
}
```

### Worker Changes

- Remove hardcoded retry logic from `UpdateJobState` (the `RetriedTimes < MaxRetries` block)
- In catch block: read `jobContext.FailureOutcome`, apply or default to Failed
- Set `jobContext.RetriedTimes` before handler execution
- `UpdateJobState` becomes pure finalization (counters, logs, cleanup based on resulting state)

### Breaking Change

Existing users with `RetryCount > 0` on `JoblyConfiguration` will need to add `AddJoblyRetry()` for retries to work. The `MaxRetries` column is still written by the publisher but no longer read by the worker for retry decisions.

## Change Manifest

| File | Change | New? |
|------|--------|------|
| `src/core/Jobly.Core/Handlers/JobFailureOutcome.cs` | Generic failure outcome POCO | Yes |
| `src/core/Jobly.Core/Handlers/IJobContext.cs` | Add `RetriedTimes`, `FailureOutcome` | |
| `src/core/Jobly.Core/Publisher.cs` | Skip `$` keys during metadata inheritance | |
| `src/core/Jobly.Worker/Retry/RetryOptions.cs` | Retry configuration | Yes |
| `src/core/Jobly.Worker/Retry/RetryPublishBehavior.cs` | Injects metadata from config | Yes |
| `src/core/Jobly.Worker/Retry/RetryPipelineBehavior.cs` | Catches failures, sets FailureOutcome | Yes |
| `src/core/Jobly.Worker/Retry/RetryServiceConfiguration.cs` | `AddJoblyRetry()` extension | Yes |
| `src/core/Jobly.Worker/JoblyWorkerService.cs` | Read outcome; remove retry logic; refactor UpdateJobState | |
| `src/core/Jobly.Worker/JoblyDispatcherWorker.cs` | Same | |
| `src/core/Jobly.Tests/Unit/RetryTests.cs` | Register retry module; update + add tests | |
| `src/core/Jobly.Tests/Integration/RetryIntegrationTests.cs` | New tests | |
| `src/core/Jobly.Tests/Integration/JoblyTestServer.cs` | Register retry module | |
