---
sidebar_position: 3
---

# Job Metadata

Attach arbitrary key-value metadata to jobs at publish time. Metadata flows through the publish pipeline, is inherited by child jobs, and is visible in the dashboard.

## Usage

### Ad-hoc metadata

Pass metadata via `JobParameters` when publishing:

```csharp
await publisher.Enqueue(new ProcessOrder { OrderId = 123 }, new JobParameters
{
    Metadata = new Dictionary<string, string>
    {
        ["tenant"] = "acme-corp",
        ["priority"] = "high",
    },
});
```

### Reading metadata in handlers

Inject `IJobContext` to access metadata during handler execution:

```csharp
public class ProcessOrderHandler : IJobHandler<ProcessOrder>
{
    private readonly IJobContext _jobContext;

    public ProcessOrderHandler(IJobContext jobContext) => _jobContext = jobContext;

    public async Task HandleAsync(ProcessOrder message, CancellationToken ct)
    {
        var tenant = _jobContext.Metadata["tenant"];
        var jobId = _jobContext.JobId;
        var traceId = _jobContext.TraceId;
        // ...
    }
}
```

`IJobContext` extends `IJobMetadata` and provides:

| Property | Type | Description |
|----------|------|-------------|
| `JobId` | `Guid` | The current job's ID |
| `TraceId` | `Guid` | The trace ID (shared across related jobs) |
| `Metadata` | `Dictionary<string, object>` | Mutable metadata dictionary attached to the job |

### Typed metadata

For type-safe access, define an interface extending `IJobMetadata`. The source generator produces a dictionary-backed implementation:

```csharp
public partial interface IOrderMetadata : IJobMetadata
{
    string CustomerName { get; set; }
    int Priority { get; set; }
    int? MaxRetries { get; set; }  // nullable for "not set" vs 0
}
```

Inject `IJobContext<IOrderMetadata>` in handlers:

```csharp
public class ProcessOrderHandler(IJobContext<IOrderMetadata> ctx) : IJobHandler<ProcessOrder>
{
    public Task HandleAsync(ProcessOrder msg, CancellationToken ct)
    {
        var name = ctx.Metadata.CustomerName;   // typed property → reads from dict
        ctx.Metadata.Priority = 5;              // typed write → writes to dict
        _ = ctx.Metadata["Priority"];           // raw dict access → same value
        return Task.CompletedTask;
    }
}
```

The typed view IS the dictionary — property writes go directly to the underlying `Dictionary<string, object>`. `IJobContext<T>` is registered as an open generic by `AddJobly()`, so no per-type registration is needed.

## Publish Pipeline Behaviors

For cross-cutting metadata (e.g., adding tenant ID to every job), implement `IPublishPipelineBehavior<T>`:

```csharp
public class TenantMetadataBehavior<T> : IPublishPipelineBehavior<T>
{
    private readonly ITenantProvider _tenantProvider;

    public TenantMetadataBehavior(ITenantProvider tenantProvider) => _tenantProvider = tenantProvider;

    public async Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        context.Metadata["tenant"] = _tenantProvider.CurrentTenantId;
        await next();
    }
}
```

Register publish pipeline behaviors the same way as handler pipeline behaviors:

```csharp
builder.Services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(TenantMetadataBehavior<>));
```

### PublishContext

The `PublishContext<T>` passed to publish pipeline behaviors contains:

| Property | Type | Description |
|----------|------|-------------|
| `Job` | `T` | The job/message being published |
| `Metadata` | `Dictionary<string, string>` | Mutable metadata dictionary — add or modify entries here |

## Metadata Inheritance

When a handler creates child jobs (continuations, batch items, or new messages), metadata from the parent job is automatically inherited by all children. This happens via the ambient `IJobContext`:

```csharp
public class ProcessOrderHandler : IJobHandler<ProcessOrder>
{
    private readonly IPublisher _publisher;

    public async Task HandleAsync(ProcessOrder message, CancellationToken ct)
    {
        // Child job automatically inherits parent's metadata (e.g., tenant=acme-corp)
        await _publisher.Enqueue(new SendReceipt { OrderId = message.OrderId });
    }
}
```

Ad-hoc metadata passed via `JobParameters` is merged with inherited metadata. If both provide the same key, the ad-hoc value wins.

## Metadata Sources (Priority Order)

When a job is published, metadata is collected from three sources:

1. **Inherited metadata** — from the parent job's execution context (if inside a handler)
2. **Ad-hoc metadata** — from `JobParameters.Metadata`
3. **Pipeline metadata** — added by `IPublishPipelineBehavior<T>` implementations

Later sources override earlier ones for the same key.

## Dashboard

The job detail page shows metadata as a formatted JSON section alongside the job payload. Metadata is only stored when non-empty — jobs without metadata have no overhead.
