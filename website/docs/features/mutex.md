---
sidebar_position: 4
---

# Mutex (Concurrency Control)

Mutexes prevent duplicate processing — only one job per key can be processing at a time. If a worker picks up a job whose mutex is already held, the job is cancelled.

## Usage

```csharp
await publisher.Enqueue(
    new ProcessPayment { CustomerId = 123 },
    new JobParameters { Mutex = "payment:123" });
```

## How It Works

1. **Enqueue** always succeeds — the mutex is not checked at publish time.
2. **Worker picks up** the job and marks it as `Processing`.
3. **Mutex check**: Before executing the handler, the worker queries if another job with the same `ConcurrencyKey` is already `Processing`.
4. **If held**: The job is set to `Deleted` with a log entry "Cancelled — mutex 'payment:123' held by another job". The worker moves on to the next job.
5. **If free**: The job executes normally.

## Race Condition Safety

The mutex check happens inside the same transaction that marks the job as `Processing`. If two workers fetch two jobs with the same mutex simultaneously, the first to commit wins — the second sees the first as Processing and cancels itself.

## Zero Overhead for Regular Jobs

Jobs without a mutex (`ConcurrencyKey = null`) skip the check entirely. No extra DB query, no performance impact.

## Use Cases

- **Payment processing**: Only one payment per customer at a time
- **Report generation**: Don't generate the same report twice simultaneously
- **External API calls**: Prevent duplicate calls to an idempotent endpoint

## Dashboard

Jobs cancelled by mutex appear as `Deleted` with a log entry explaining which mutex key was held. The job detail page shows the mutex key in the Details card.

## Future: Semaphores & Rate Limiting

The `ConcurrencyKey` field is designed for extension. The same column will support:
- **Semaphores**: Max N concurrent jobs per key (not just 1)
- **Rate limiting**: Max N executions per time window per key
