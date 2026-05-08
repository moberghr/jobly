# Brainstorm: Semaphore Addon for Warp

Date: 2026-05-08
Status: Converged. Spec drafting is the next step.

## Problem

Warp has a `Mutex` addon (one job per key Processing at a time). It does not have a way to say *"up to N jobs per key Processing at a time"* — the standard "limit concurrent calls to an external API" pattern. Add a Semaphore addon mirroring Mutex's surface but with a configurable slot count.

## Reference patterns studied

- **Hangfire.Throttling (commercial)** — `[Semaphore("key")]` attribute + `IThrottlingManager.AddOrUpdateSemaphore(name, limit)`. Slot rows stored in the same backing store as jobs; release-and-state-transition share a transaction. On contention default action is `RetryJob` (back to `Scheduled` with ~1m delay) or `DeleteJob`. Strict mode keeps slot through `Failed` so retries don't overrun. No fairness — issue #1921 documents `Scheduled→Enqueued→Scheduled` ping-pong under heavy contention.
- **Hangfire.MaximumConcurrentExecutions (OSS)** — opposite design: blocks the worker thread in a poll loop on N named distributed locks. Throws after 60s.
- **Sidekiq Enterprise** — `Sidekiq::Limiter.concurrent(name, N, wait_timeout, lock_timeout)`. BLPOP-blocks briefly then raises; middleware reschedules with linear backoff. `lock_timeout` is the lease.
- **Faktory Enterprise** — semaphore as a fetch-time predicate: worker doesn't fetch until a slot is free; job stays at queue head, FIFO preserved. Most efficient design but requires worker-fetch SQL changes.
- **BullMQ rate-limit** — Redis TTL key, jobs stay in `waiting` state.
- **River, Quartz.NET, Jobby** — only mutex-flavor (uniqueness or `DisallowConcurrentExecution`); no per-key slot semaphore.

## Key design takeaways

- Slot should be a row owned by `JobId` (Hangfire/Faktory shape), not a counter — self-healing on stale-recovery.
- Lease: reuse Warp's `LastKeepAlive` / `StaleJobRecovery` rather than inventing new lease infra.
- **Don't block the worker thread.** "Reschedule with delay" or "don't fetch" are the two correct patterns; blocking wastes a worker slot.
- Strict mode (don't release on `Failed`) is genuinely useful, pairs naturally with the Retry addon.

## Decision: mirror Mutex Wait mode, scaled to N

PR #159 (`75b557a`, "Add Mutex Wait mode for per-key job serialization") on `main` already established the dual-mode pattern for Mutex:

```csharp
public enum MutexMode { Skip = 1, Wait = 2 }
```

- `Skip` (default) — duplicate goes to `Deleted` (the original Mutex behavior).
- `Wait` — duplicate goes back to `Enqueued` with `ScheduleTime = now`, audit-logged as `Requeued`.

`stats:requeued` counter is emitted globally by the worker for any `Enqueued`/`Scheduled` outcome and surfaces on the new Counters dashboard page.

Semaphore v1 will mirror this 1-for-1 with `SemaphoreMode { Skip = 1, Wait = 2 }`, using `IDistributedSemaphoreProvider` from `Medallion.Threading` (both Postgres and SQL Server providers expose it) to manage slots. The pipeline behavior calls `TryAcquireAsync(name, maxCount, timeout: 0)` instead of `TryAcquireAsync(name, timeout: 0)`. Wait mode reuses the existing requeue `JobOutcome` shape — the `stats:requeued` counter and Counters page work for free.

## Diverges from Mutex by adding an admin layer

Unlike Mutex (limit is always 1, hard-coded), Semaphore limit is the value most likely to change at runtime ("scale up to 10 concurrent payment-processor calls during business hours"). v1 ships a `Semaphore` entity with a `Limit` column, an `ISemaphoreManager` service (`AddOrUpdateSemaphore`, `RemoveSemaphore`, `GetSemaphore`, list), and a `Semaphores` dashboard page mirroring the Counters page pattern.

**Precedence:** if an admin row exists for the key, its `Limit` overrides the attribute/extension `Limit`. If no admin row exists, the attribute/extension `Limit` is used. This is the source-of-truth rule operators expect when scaling concurrency without redeploying.

## v1 scope (locked)

- `[Semaphore("key", limit)]` attribute with `Mode` init-only property (default `Wait`).
- `.WithSemaphore("key", limit, mode)` extension on `JobParameters`.
- No format-string templating — users interpolate at the call site like Mutex.
- `SemaphoreMode { Skip = 1, Wait = 2 }`. Default `Wait`.
- `IWarpSemaphoreProvider` abstraction; provider packages register Postgres / SQL Server implementations wrapping `Medallion.Threading.IDistributedSemaphoreProvider`.
- `Semaphore` entity, EF Core configuration, EF migrations on both providers.
- `ISemaphoreManager` service: `AddOrUpdateSemaphore(name, limit)`, `RemoveSemaphore(name)`, `GetSemaphore(name)`, `ListSemaphores()`.
- Dashboard page at `/warp/semaphores` mirroring the Counters page — list, edit limit inline, delete.
- Strict release mode (default on): slot held through `Failed` so retries don't overrun.
- Tests on both Postgres + SQL Server (unit + integration), full Mutex-style coverage.
- Docs page at `website/docs/features/semaphore.md` + dashboard page docs at `website/docs/ui/semaphores.md`.

## v1 explicit out-of-scope

- Strict FIFO / fairness across queued jobs sharing a key (best-effort only, same as Mutex).
- Format-string templating in attribute keys (`{0}`, `{1}`).
- Worker-fetch-level filtering (Faktory-style "don't fetch"). Explicitly violates §6.1 *worker hot path is sacred*; revisit only if churn becomes measurable.
- Mutex retrofit beyond what already exists in PR #159.
- Per-call lease/timeout overrides (the existing `LastKeepAlive` / `StaleJobRecovery` cadence handles slot recovery on crash).

## Risks called out

- **Saturation churn.** Hangfire issue #1921 documents `Scheduled→Enqueued` ping-pong under heavy contention. Mitigations: small reschedule delay with jitter, monitoring via `stats:requeued`, and clear docs that recommend increasing the limit if `requeued` rate exceeds `succeeded`.
- **Slot leak on missed releases.** Mitigated by `IDistributedSemaphoreProvider`'s lease (Medallion.Threading uses keepalive on the underlying connection) plus existing stale-job recovery. Worth adding a startup audit that warns on orphaned slot rows.
- **Migration on both providers.** Adding a new entity is a real schema change; Postgres + SQL Server migrations both need to land in the same PR.
- **Admin precedence subtlety.** The "admin row > attribute" rule needs to be documented prominently, otherwise users who edit the dashboard limit will be confused when a redeploy with a stale attribute appears to take effect (it doesn't — admin row wins).
