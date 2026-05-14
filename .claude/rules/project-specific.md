# Project-Specific Patterns

## Configuration

- **§8.1** `WarpConfiguration` via `IOptions<WarpConfiguration>`. All configurable values go through this pattern. The builder (`AddWarp<TContext>(opt => ...)`) inherits from `WarpConfiguration`, so config fields are set directly on `opt`.

## Job Lifecycle

- **§8.2** Failed jobs never auto-delete (`ExpireAt = null`). Only explicit user action (dashboard delete/requeue) or count-based cleanup (`MaxExpirableJobCount`, default null/disabled — deletes oldest by `ExpireAt` when threshold exceeded; failed jobs excluded).
- **§8.3** `ContinuationOptions` is generalized to all job kinds — any job with children can control child activation on parent failure.
- **§8.4** `RequeueJob` resets `ScheduleTime` to now. Requeued jobs always execute immediately.
- **§8.5** Cancellation uses `CancellationMode` enum (`None=0, Graceful=1`), not an immediate state change. `DeleteJob` sets `CancellationMode = Graceful`; `RunJobMonitor` detects this and cancels the handler's `CancellationToken`. If the handler ignores the token and completes, state stays `Completed` (work happened) and `CancellationMode` is cleared. Dashboard shows "Cancelling..." badge while waiting.

## Addons (Mutex, Semaphore, Timeout, RateLimit, CircuitBreaker, DB Push)

- **§8.6** Concurrency control is opt-in via `opt.AddConcurrency()`. `[Mutex("k")]` (limit fixed at 1) and `[Semaphore("k", N)]` (limit > 1) share `ConcurrencyPipelineBehavior`. Two policies via `ConcurrencyMode`: `Skip` (`[Mutex]` default — surplus → `Deleted`) and `Wait` (`[Semaphore]` default — surplus requeued). **Disjoint namespaces by design:** `[Mutex("k")]` uses `warp:concurrency:k`; `[Semaphore("k", N)]` uses `warp:concurrency:k:0..k:{N-1}`. Mixing both against the same key produces independent limits. Admin overrides via `IConcurrencyLimitManager` (precedence: `admin row > meta.Limit > 1`). Hide-on-404 dashboard nav.
- **§8.7** Timeout is opt-in via `opt.AddTimeout()`. `[Timeout(seconds: N)]` or `WithTimeout(TimeSpan)`. Two modes (`TimeoutMode`): `Delete` (default — pipeline sets `Deleted`, **not retried** by `AddRetry`) and `Fail` (throws `TimeoutException`, **caught** by `AddRetry`). Two scopes (`TimeoutScope`): `PerAttempt` (fresh budget per retry) and `Total` (`DeadlineUtc` stamped on publish). **Ordering: `AddRetry()` MUST come before `AddTimeout()`** (§2.12).
- **§8.8** Rate limiting is opt-in via `opt.AddRateLimit()`. Two styles (`Style`): `Fixed` (wall-clock window, floor-aligned to global UTC ticks) and `Sliding` (rolling window over last N starts, defensively trimmed). Two policies (`Mode`): `Skip` (default — `Deleted`) and `Wait` (rescheduled via `JobOutcome.RescheduledState`). `perSeconds` capped at `MaxWindowSeconds` (7 days). Lock is released after check-and-increment — **not** held during handler execution (unlike Mutex). Live state in `RateLimitBucket` entity. Lock-contention reschedule has 100–500ms jitter. **DB push does NOT accelerate Wait-mode reschedules** — they land in `State.Scheduled` and depend on `ScheduledJobActivation` polling. **Ordering: `AddConcurrency()` before `AddRateLimit()`** so mutex rejects don't waste tokens.
- **§8.9** `RecurringJobScheduler` creates jobs with `ScheduleTime = now`. `AddOrUpdateRecurringJob` only registers/updates the definition — does **not** create jobs. Acquires a distributed lock on the recurring-job name and saves immediately (deliberate §5.7 exception). `RecurringJobLog` is the immutable audit trail; `ExpirationCleanup` retains the last 100 logs per recurring job.

## Conventions

- **§8.10** Source generators (`Warp.SourceGenerator`, `Warp.Http.SourceGenerator`) target `netstandard2.0` and provide zero-allocation mediator dispatch and HTTP endpoint generation. `[WarpHttpGet/Post/Put/Patch/Delete]` annotations on handler classes emit `RequestDelegate`s; `IJob` / `IMessage` rejected at compile time (`WHTTP001`).
- **§8.11** **Enums always start at 1, never 0.** `Skip = 1, Wait = 2`, never `Skip = 0`. Avoids `default(T)` collisions and forces explicit assignment. Applies to `JobKind`, `State`, `CancellationMode`, `ConcurrencyMode`, `RateLimitMode`, `RateLimitStyle`, `TimeoutMode`, `TimeoutScope`, etc.
- **§8.12** **Metadata properties are addon-prefixed**, not bare names. `IRateLimitMetadata.RateLimitKey` (not `Key`), `IRateLimitMetadata.RateLimitWindowSeconds` (not `WindowSeconds`), `IConcurrencyMetadata.ConcurrencyKey` / `ConcurrencyLimit` / `ConcurrencyMode`. Prevents dict-key collisions when a single job carries multiple metadata interfaces. Avoid `Dictionary<,>` names entirely — they're a red flag for collision-prone APIs.
- **§8.13** Entity namespaces are **split**: `Job` lives in `Warp.Core.Entities`; `JobLog`, `Server`, `Worker`, `ServerTask`, `ServerLog`, `RecurringJob`, `RecurringJobLog`, `RateLimitBucket`, `RateLimitOverride`, `ConcurrencyLimit` etc. live in `Warp.Core.Data.Entities`. Same folder, different namespaces — do not collapse them.
- **§8.14** **Routed `IMessage` jobs must keep `HandlerType` on requeue.** Addons that commit inside handler scope must capture-save-fire and clear-tracker-on-conflict (saga correctness pitfalls).
- **§8.15** Real-time log flushing: `RunJobMonitor` drains `JobLogCollector` every ~1s during handler execution and persists logs to the database. Logs visible in the dashboard while the job is still processing.
- **§8.16** `MetadataConvert` was string-only until the DateTime branch added 2026-05-12 — non-primitive `IJobMetadata` properties need explicit roundtrip tests.
