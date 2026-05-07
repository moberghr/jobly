---
sidebar_position: 4
---

# Mutex (Concurrency Control)

Mutexes prevent duplicate processing — only one job per key can be processing at a time. If a worker picks up a job whose mutex is already held, the job is either cancelled or requeued depending on the configured `MutexMode`.

## Guarantees and limits

What Mutex **does** guarantee:

- **Mutual exclusion per key** — at most one job per key in `Processing` state at any moment, across all workers and servers (enforced by `IWarpLockProvider`).
- **Zero overhead** for jobs that don't set a key — the pipeline behavior short-circuits before touching the lock provider.

What Mutex **does not** guarantee:

- **No execution order across jobs sharing a key.** Neither mode preserves submission order. In `Skip` mode the loser is dropped, so order is moot. In `Wait` mode multiple workers race on the requeue write, so the order in which queued jobs eventually run can drift from submission order under contention. For light, bursty traffic the requeue timestamps usually keep things roughly in order, but this is best-effort and **not part of the contract**.
- **No fairness or starvation prevention.** A constantly re-arriving stream of jobs for the same key can starve a long-blocked one indefinitely (whichever job a worker happens to pick wins).

If you need strict FIFO per key, Mutex isn't the right primitive — that requires fetch-time filtering, which Warp doesn't currently expose.

## Setup

Mutex is an opt-in addon. Register it inside the `AddWarpWorker` lambda:

```csharp
builder.Services.AddWarpWorker<AppDbContext>(opt =>
{
    opt.UsePostgreSql();
    opt.AddMutex();
});
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

## Modes: Skip vs Wait

`MutexMode` controls what happens when a job is picked up while the mutex is held:

- **`MutexMode.Skip`** (default) — the duplicate is short-circuited to `Deleted`. Useful for deduplication patterns where running the same work twice is wasteful or unsafe.
- **`MutexMode.Wait`** — the duplicate is requeued (`State = Enqueued`, `ScheduleTime = now`) and the audit log records a `Requeued` entry. The job will be picked up again on a later fetch and re-attempts the lock. This gives you mutual exclusion without losing work.

```csharp
// Wait mode via fluent API
await publisher.Enqueue(
    new HandleTelegramUpdate { UserId = 123 },
    new JobParameters().WithMutex("user:123", MutexMode.Wait));

// Wait mode via attribute
[Mutex("user-handler", Mode = MutexMode.Wait)]
public class HandleTelegramUpdate : IJob
{
    public int UserId { get; set; }
}
```

## How It Works

Mutex is implemented as a `MutexPipelineBehavior` — a pipeline behavior that wraps handler execution:

1. **Enqueue** always succeeds — the mutex is not checked at publish time.
2. **Worker picks up** the job and marks it as `Processing`.
3. **Pipeline runs**: `MutexPipelineBehavior` attempts to acquire a distributed lock keyed by `warp:mutex:{key}`.
4. **If held**: The behavior sets `IJobContext.Outcome` according to the configured `MutexMode`. `Skip` → `Deleted` with a log entry "Cancelled — mutex 'payment:123' held by another job". `Wait` → `Enqueued` with `ScheduleTime = now` and a `Requeued` log entry "Requeued — mutex 'user:123' held by another job".
5. **If free**: The lock is acquired, the handler executes, and the lock is released when the handler completes (or fails).

## Race Condition Safety

The distributed lock (via `IWarpLockProvider`) ensures mutual exclusion across all workers and servers. If two workers fetch two jobs with the same mutex key simultaneously, the first to acquire the lock wins — the second sees the lock as held and cancels.

## Zero Overhead for Regular Jobs

Jobs without a mutex key skip the lock check entirely. The behavior reads the metadata, finds no key, and calls the next behavior immediately.

## Use Cases

**`Skip` mode (deduplication):**
- **Report generation**: Don't generate the same report twice simultaneously
- **External API calls**: Prevent duplicate calls to an idempotent endpoint
- **Cache refresh**: Drop concurrent refresh requests for the same key

**`Wait` mode (per-key serialization):**
- **Per-user message handling**: Process updates from the same user one at a time, while different users run in parallel
- **Per-aggregate state machines**: Avoid two writers stomping on the same aggregate row
- **Payment processing**: Serialize payments per customer rather than dropping duplicates

## Dashboard

Jobs cancelled by mutex (`Skip` mode) appear as `Deleted` with a log entry explaining which mutex key was held. Jobs requeued by mutex (`Wait` mode) appear in the audit trail as `Requeued` and continue retrying until the key is free. The mutex key is visible in the job's metadata section on the detail page.
