# Spec: Retry as External Pipeline Module + Typed Metadata

## Delivered

### Problem
1. Retry logic was hardcoded in the worker — not configurable, composable, or extensible.
2. Failed jobs retried immediately on the same worker with no delay.
3. Metadata was `Dictionary<string, string>` — stringly typed, no type safety.
4. Handler and worker shared a DbContext — handler's partial changes leaked on failure.

### Solution

**Retry as pipeline module:** `AddWarpRetry()` registers `RetryPipelineBehavior` (catches failures, reads typed metadata, sets `FailureOutcome`) and `RetryPublishBehavior` (injects retry config at publish time). Warp Core has zero retry knowledge.

**Typed metadata:** `IJobMetadata` marker interface + source generator produces `Dictionary<string, object>` subclasses with typed property accessors. `IJobContext.GetMetadata<T>()` provides typed access. `MetadataSerializer` uses a custom `JsonConverter` for native types.

**Worker scope isolation:** Worker and handler use separate DI scopes. Handler's DbContext changes are committed on success (outbox), discarded on failure.

### Architecture

```
Publish: RetryPublishBehavior → metadata["MaxRetries"] = 3

Execute: workerScope creates handlerScope
  → RetryPipelineBehavior wraps handler (IJobContext.GetMetadata<IRetryMetadata>())
    → Handler runs
    ← Handler throws
  ← RetryPipelineBehavior: meta.RetriedTimes++, sets FailureOutcome
  Worker reads FailureOutcome, serializes metadata, saves
  handlerScope disposed
```

### User API

```csharp
// Registration
services.AddWarpWorker<AppDbContext>();
services.AddWarpRetry(o => { o.MaxRetries = 3; o.Delays = [15, 60, 300]; });

// Custom typed metadata (source-generated)
public partial interface IOrderMetadata : IJobMetadata
{
    string CustomerName { get; set; }
    int Priority { get; set; }
}

// Handler with typed access
public class MyHandler(IJobContext ctx) : IJobHandler<MyJob>
{
    public Task HandleAsync(MyJob msg, CancellationToken ct)
    {
        var meta = ctx.GetMetadata<IOrderMetadata>();
        meta.CustomerName = "John";  // typed, writes to dict
    }
}
```
