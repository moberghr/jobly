---
sidebar_position: 4
---

# Mutex (Concurrency Control)

Mutexes prevent duplicate processing — only one job per key can be processing at a time. If a worker picks up a job whose mutex is already held, the job is cancelled.

## Setup

Mutex is an opt-in addon. Register it alongside `AddWarpWorker`:

```csharp
builder.Services.AddWarpWorker<AppDbContext>();
builder.Services.AddWarpMutex();
```

## Usage

Set the mutex key at publish time using the `.WithMutex()` extension:

```csharp
await publisher.Enqueue(
    new ProcessPayment { CustomerId = 123 },
    new JobParameters().WithMutex("payment:123"));
```

Or use the `[Mutex]` attribute for a static key on the job class:

```csharp
[Mutex("payment-processing")]
public class ProcessPayment : IJob
{
    public int CustomerId { get; set; }
}

// Enqueue normally — key comes from the attribute
await publisher.Enqueue(new ProcessPayment { CustomerId = 123 });
```

You can also set the key via typed metadata:

```csharp
await publisher.Enqueue(new ProcessPayment { CustomerId = 123 },
    new JobParameters().Configure<IMutexMetadata>(m => m.ConcurrencyKey = "payment:123"));
```

## How It Works

Mutex is implemented as a `MutexPipelineBehavior` — a pipeline behavior that wraps handler execution:

1. **Enqueue** always succeeds — the mutex is not checked at publish time.
2. **Worker picks up** the job and marks it as `Processing`.
3. **Pipeline runs**: `MutexPipelineBehavior` attempts to acquire a distributed lock keyed by `warp:mutex:{key}`.
4. **If held**: The behavior sets `IJobContext.Outcome` to `Deleted` and returns without calling the handler. The worker finalizes the job as Deleted with a log entry "Cancelled — mutex 'payment:123' held by another job".
5. **If free**: The lock is acquired, the handler executes, and the lock is released when the handler completes (or fails).

## Race Condition Safety

The distributed lock (via `IWarpLockProvider`) ensures mutual exclusion across all workers and servers. If two workers fetch two jobs with the same mutex key simultaneously, the first to acquire the lock wins — the second sees the lock as held and cancels.

## Zero Overhead for Regular Jobs

Jobs without a mutex key skip the lock check entirely. The behavior reads the metadata, finds no key, and calls the next behavior immediately.

## Use Cases

- **Payment processing**: Only one payment per customer at a time
- **Report generation**: Don't generate the same report twice simultaneously
- **External API calls**: Prevent duplicate calls to an idempotent endpoint

## Dashboard

Jobs cancelled by mutex appear as `Deleted` with a log entry explaining which mutex key was held. The mutex key is visible in the job's metadata section on the detail page.
