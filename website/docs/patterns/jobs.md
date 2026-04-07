---
sidebar_position: 2
---

# Jobs

Jobs implement `IJob` and have a **single handler**. They support scheduling, retries, continuations, batches, named queues, and mutex.

## Define a job

```csharp
public class GenerateReport : IJob
{
    public int ReportId { get; set; }
}

public class GenerateReportHandler : IJobHandler<GenerateReport>
{
    public async Task HandleAsync(GenerateReport message, CancellationToken ct)
    {
        // Generate the report
    }
}
```

## Enqueue

```csharp
await publisher.Enqueue(new GenerateReport { ReportId = 1 });
```

## Schedule

```csharp
await publisher.Schedule(new GenerateReport { ReportId = 1 }, DateTime.UtcNow.AddHours(1));
```

## Retries

```csharp
await publisher.Enqueue(new GenerateReport { ReportId = 1 }, maxRetries: 3);
```

Failed jobs are retried automatically. Crash requeues (server died mid-execution) do **not** count against the retry limit.

## Named Queues

```csharp
await publisher.Enqueue(new GenerateReport { ReportId = 1 }, queue: "critical");
```

Queues are processed in alphabetical order. A worker subscribes to specific queues:

```csharp
options.Queues = new[] { "a-critical", "b-default", "c-low" };
```

## Continuations

```csharp
var parentId = await publisher.Enqueue(new ProcessPayment { OrderId = 1 });
await publisher.Enqueue(new SendReceipt { OrderId = 1 }, parentId); // Runs after parent completes
```

## Batches

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
await batchPublisher.StartNew(jobs, ContinuationOptions.OnlyOnSucceeded);

// Fire when all jobs finish, regardless of success/failure
await batchPublisher.StartNew(jobs, ContinuationOptions.OnAnyFinishedState);
```

## Recurring Jobs

Register a cron-based recurring job:

```csharp
await recurringPublisher.AddOrUpdateRecurringJob(
    new CleanupSessions(), name: "session-cleanup", cron: "0 * * * *");
```

This only registers the definition. The `RecurringJobSchedulerTask` creates jobs when the cron time arrives. See [Recurring Jobs](/docs/recurring-jobs) for full details.

## Cancellation

Cancel a running job gracefully:

```csharp
await jobCommandService.DeleteJob(jobId);
```

If the job is processing, this sets `CancellationMode = Graceful` instead of immediately changing state. The worker detects it and cancels the handler's `CancellationToken`. See [Job Cancellation](/docs/cancellation) for the full flow.

## Mutex

Only one job per mutex key can be processing at a time:

```csharp
await publisher.Enqueue(new ProcessPayment { CustomerId = 123 },
    new JobParameters { Mutex = "payment:123" });
```

See [Mutex](/docs/mutex) for details.
