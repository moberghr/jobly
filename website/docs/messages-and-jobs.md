---
sidebar_position: 2
---

# Messages, Jobs & Requests

Jobly supports three patterns. Use the ones that fit your use case, or mix all three.

## Messages (Pub/Sub)

Messages implement `IMessage` and can have **multiple handlers**. When published, the worker discovers all registered handlers and creates a separate job for each.

```csharp
// Define a message
public class OrderPlaced : IMessage { public int OrderId { get; set; } }

// Multiple handlers — each runs independently
public class SendConfirmationEmail : IMessageHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced message, CancellationToken ct)
    {
        // Send email
    }
}

public class NotifyWarehouse : IMessageHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced message, CancellationToken ct)
    {
        // Notify warehouse
    }
}

// Publish
await publisher.Publish(new OrderPlaced { OrderId = 123 });
```

## Jobs (Orchestration)

Jobs implement `IJob` and have a **single handler**. They support scheduling, retries, continuations, batches, and named queues.

```csharp
public class GenerateReport : IJob { public int ReportId { get; set; } }

public class GenerateReportHandler : IJobHandler<GenerateReport>
{
    public async Task HandleAsync(GenerateReport message, CancellationToken ct)
    {
        // Generate the report
    }
}
```

### Enqueue

```csharp
await publisher.Enqueue(new GenerateReport { ReportId = 1 });
```

### Schedule

```csharp
await publisher.Schedule(new GenerateReport { ReportId = 1 }, DateTime.UtcNow.AddHours(1));
```

### Retries

```csharp
await publisher.Enqueue(new GenerateReport { ReportId = 1 }, maxRetries: 3);
```

Failed jobs are retried automatically. Crash requeues (server died mid-execution) do **not** count against the retry limit.

### Named Queues

```csharp
await publisher.Enqueue(new GenerateReport { ReportId = 1 }, queue: "critical");
```

Queues are processed in alphabetical order. A worker subscribes to specific queues:

```csharp
options.Queues = new[] { "a-critical", "b-default", "c-low" };
```

### Continuations

```csharp
var parentId = await publisher.Enqueue(new ProcessPayment { OrderId = 1 });
await publisher.Enqueue(new SendReceipt { OrderId = 1 }, parentId); // Runs after parent completes
```

### Batches

```csharp
var batchPublisher = serviceProvider.GetRequiredService<IBatchPublisher>();

var jobs = orders.Select(o => new ProcessOrder { OrderId = o.Id }).ToList();
var batchId = await batchPublisher.StartNew(jobs);

// Continuation after batch completes
var followUps = new List<SendSummary> { new() };
await batchPublisher.ContinueBatchWith(followUps, batchId);
```

Batch continuation options:

```csharp
// Default: continuation only fires when ALL jobs succeed
await batchPublisher.StartNew(jobs, BatchContinuationOptions.OnlyOnSucceeded);

// Fire when all jobs finish, regardless of success/failure
await batchPublisher.StartNew(jobs, BatchContinuationOptions.OnAnyFinishedState);
```

### Recurring Jobs

Register a cron-based recurring job:

```csharp
await recurringPublisher.AddOrUpdateRecurringJob(
    new CleanupSessions(), name: "session-cleanup", cron: "0 * * * *");
```

This only registers the definition. The `RecurringJobSchedulerTask` creates jobs when the cron time arrives. See [Recurring Jobs](./recurring-jobs.md) for full details.

### Cancellation

Cancel a running job gracefully:

```csharp
await jobCommandService.DeleteJob(jobId);
```

If the job is processing, this sets `CancellationMode = Graceful` instead of immediately changing state. The worker detects it and cancels the handler's `CancellationToken`. See [Job Cancellation](./cancellation.md) for the full flow.

## Requests (In-Memory)

Requests implement `IRequest<TResponse>` and have a **single handler** that returns a typed response. Unlike jobs and messages, requests are **not persisted to the database** — they execute immediately in-process via `IMediator.Send()`.

Use requests for queries, commands that need a response, or any synchronous in-process work that benefits from the pipeline.

```csharp
// Define a request with a response type
public class GetUser : IRequest<UserDto>
{
    public int UserId { get; set; }
}

// Single handler that returns TResponse
public class GetUserHandler : IRequestHandler<GetUser, UserDto>
{
    private readonly AppDbContext _db;

    public GetUserHandler(AppDbContext db) => _db = db;

    public async Task<UserDto> HandleAsync(GetUser request, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync(request.UserId, ct);
        return new UserDto { Id = user.Id, Name = user.Name };
    }
}

// Send via IMediator
var user = await mediator.Send(new GetUser { UserId = 1 });
```

### Key differences from Jobs/Messages

| | Jobs/Messages | Requests |
|---|---|---|
| Storage | Persisted to database | In-memory only |
| Execution | Background worker | Immediate, in-process |
| Response | None (Unit) | Typed TResponse |
| Retries | Automatic | None (caller handles) |
| Dashboard | Visible in UI | Not visible |

### Type hierarchy

All types share a common base:

```csharp
public interface IRequest<out TResponse>;     // Base
public interface IJob : IRequest<Unit>;        // Persistent, single handler
public interface IMessage : IRequest<Unit>;    // Persistent, multiple handlers
// IRequest<TResponse> used directly           // In-memory, returns TResponse
```

## Pipeline Behaviors

Pipeline behaviors wrap all handler invocations — jobs, messages, **and requests** — through a unified interface:

```csharp
public class LoggingBehavior<T, TResponse> : IPipelineBehavior<T, TResponse>
    where T : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<T, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<T, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> HandleAsync(T request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _logger.LogInformation("Starting {Type}", typeof(T).Name);
        var result = await next();
        _logger.LogInformation("Completed {Type}", typeof(T).Name);
        return result;
    }
}
```

Register as an open generic:

```csharp
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

The pipeline applies to all three patterns. For jobs and messages, `TResponse` is `Unit`. For requests, it's your custom response type. You can also target specific types:

```csharp
// Only for GetUser requests
public class CacheBehavior : IPipelineBehavior<GetUser, UserDto> { ... }

// Only for jobs (any IJob)
public class RetryBehavior<T> : IPipelineBehavior<T, Unit> where T : IJob { ... }
```

Logger output from pipeline behaviors appears in the job detail "Handler Output" section.
