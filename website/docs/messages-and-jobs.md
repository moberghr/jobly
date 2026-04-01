---
sidebar_position: 2
---

# Messages & Jobs

Jobly supports two publishing patterns. Use the one that fits your use case, or mix both.

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

## Pipeline Behaviors

Pipeline behaviors wrap all handler invocations (both messages and jobs):

```csharp
public class LoggingBehavior<T> : IPipelineBehavior<T> where T : class
{
    private readonly ILogger<LoggingBehavior<T>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<T>> logger) => _logger = logger;

    public async Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken ct)
    {
        _logger.LogInformation("Starting {Type}", typeof(T).Name);
        await next();
        _logger.LogInformation("Completed {Type}", typeof(T).Name);
    }
}
```

Register as an open generic:

```csharp
builder.Services.AddTransient(typeof(IPipelineBehavior<>), typeof(LoggingBehavior<>));
```

Logger output from pipeline behaviors appears in the job detail "Handler Output" section.
