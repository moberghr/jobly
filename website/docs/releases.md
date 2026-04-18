---
sidebar_position: 6
---

# Releases

## 0.8.0

*Unreleased*

### New Features

- **Exponential Polling Backoff** — Workers and the batch-fetch dispatcher now back off geometrically when queues are idle, reducing database load during quiet periods. Configure via `MaxPollingInterval` (default `30s`, ceiling) and `PollingIntervalFactor` (default `2.0`, multiplier). `PollingInterval` becomes the floor. On any processed job, the delay resets to the floor instantly, so throughput under load is unchanged. Paused workers stay at the floor (no compounding while paused). Available on both top-level `JoblyWorkerConfiguration` and per-group `WorkerGroupConfiguration`. Set `PollingIntervalFactor = 1.0` to disable backoff. See [Operations → Configuration → Exponential Polling Backoff](/docs/operations/configuration#exponential-polling-backoff).
- **Retry Jitter** — New `JitterFactor` option on `RetryOptions` applies multiplicative random jitter to each computed retry delay: `delay * (1 + JitterFactor * rand(-1, 1))`. Clamped to `[0, 1]`. Global only — no per-job override. Defaults to `0.0` (no jitter) so existing behavior is unchanged. Use to spread retry attempts and avoid thundering herds when many jobs fail at once (e.g. downstream outage). The actual jittered `ScheduleTime` is recorded in the `Requeued` JobLog entry so operators can diagnose from the dashboard.
- **NoRestart (stale-recovery opt-out)** — New addon `AddJoblyNoRestart()` lets specific job types stay `Failed` on worker crash instead of being auto-requeued. Apply with `[NoRestart]` / `[Restart]` attributes on the job class (inherits through the class hierarchy), `.WithRestart(bool)` per-enqueue, or flip the global default with `RestartStaleJobsByDefault = false`. Override chain: per-publish > attribute > global. For non-idempotent work (payments, emails, webhooks). See [NoRestart](/docs/features/no-restart).
- **Circuit Breaker** — New addon `AddJoblyCircuitBreaker<TContext>()` stops hammering a failing downstream when failures cross a threshold. Opens after `Threshold` consecutive failures, stays open for `Duration`, then transitions to a **half-open state with an atomic probe gate** — exactly one worker probes while others reschedule, preventing thundering herd on recovery. Customise per-handler via `[CircuitBreaker(Group = "...", Threshold = N)]`. Adds a `CircuitBreakerState` entity to your DbContext (migration required). See [Circuit Breaker](/docs/features/circuit-breaker).
- **Batched Completions (Dispatcher Mode)** — When `UseDispatcher = true`, each worker now buffers job completions in memory and commits them as a single multi-row transaction, collapsing N per-job commits into one. Tune via `CompletionBatchSize` (default `50`) and `CompletionFlushInterval` (default `100ms`); set `CompletionBatchSize = 1` to opt out. Poison-entry isolation: a single bad row in a batch of 50 is dropped via recursive split; the other 49 still commit. SIGTERM mid-flush is safe — `FlushAsync` commits to completion using `CancellationToken.None` internally. Trade-off is at-least-once semantics; pair with `[NoRestart]` for non-idempotent handlers. See [Batched Completions](/docs/features/batched-completions).
- **Configurable Log Flush Interval** — New `LogFlushInterval` on `JoblyWorkerConfiguration` (default `1s`) controls how often the job monitor drains handler `ILogger` output into the JobLog table. Lower values surface dashboard logs faster at the cost of more DB writes.

### Improvements

- **Resilient worker/dispatcher exception handling** — `JoblyWorker` now catches transient exceptions in its poll loop (matching the dispatcher), so a single DB hiccup or handler pipeline fault no longer silently terminates the BackgroundService. The exception path uses a fixed floor delay instead of compounding the polling backoff, so jobs resume within `PollingInterval` of recovery instead of sitting at `MaxPollingInterval` for 30s.
- **`[NoRestart]` and `[Restart]` attributes are inherited** — Declare the policy on a base class (e.g. `PaymentJobBase`) and every derived concrete job inherits it. A derived class with its own attribute overrides the base — closest direct declaration wins.
- **Circuit breaker exception handling narrowed** — The `DbUpdateException` catch in `CircuitBreakerStore.RecordFailureAsync` now only suppresses unique-constraint violations (Npgsql `23505`, SqlClient `2627`/`2601`). CHECK, FK, and column-length violations propagate instead of being silently swallowed.
- **Test suite 48% faster, flake-free** — Disabled idle-polling backoff inside the test server, tuned `StaleJobRecoveryInterval` for tests only, made `LogFlushInterval` configurable, and bumped the `TimedFact` default from 10s to 30s. Full suite (947 tests) runs in ~2m 39s (was ~5m 05s). Eliminated the latent `TimedFact`/`WaitForJobState` timeout-mismatch class of flakes.

### Bug Fixes

- **Dispatcher SIGTERM completion-loss** — Pre-fix, `CompletionBatch.FlushAsync` drained its buffer before observing cancellation, and a shutdown mid-flush silently dropped the drained batch. Now commits with `CancellationToken.None` internally.
- **Circuit breaker thundering herd on half-open** — Without the new HalfOpen CAS, every worker polling when `OpenUntil` lapsed fired a concurrent probe against the recovering downstream. Fix guarantees exactly one probe fires per recovery window.
- **Retry jitter `ScheduleTime` not logged** — The `Requeued` JobLog entry now includes `(next attempt scheduled: <ISO timestamp>)` so operators can see the actual delay jitter applied.
- **MultiServer cross-test flakiness** — A too-aggressive `StaleJobRecoveryInterval` in the test fixture raced worker keep-alive refreshes under two-server SQL Server load, producing sporadic `DbUpdateConcurrencyException`.

### Migration

- **Circuit Breaker migration** — If you register `AddJoblyCircuitBreaker<TContext>()`, a new `CircuitBreakerState` entity is added to your DbContext. Run an EF Core migration to create the table (`jobly.circuit_breaker_state` under default schema + snake_case naming).
- **`CompletionBatch.FlushAsync` parameter removal** — If you were calling `CompletionBatch.FlushAsync(CancellationToken)` directly (rare — it's internal to `Jobly.Worker`), the parameter has been removed. The method now always commits to completion.
- **`[NoRestart]` / `[Restart]` now inherit** — Behaviour change: a base class decorated with `[NoRestart]` now affects every derived concrete job. If you relied on the pre-release `Inherited = false` behaviour, add `[Restart]` on the specific derived types you want to opt back in.
- **`StaleJobRecoveryTask.RequeueStaleJobs` removed** — Replaced by `RecoverStaleJobs` returning `StaleJobRecoveryResult` (includes `Requeued`, `Failed`, `Deleted` counts). No `[Obsolete]` bridge; callers must migrate to the new signature.

---

## 0.7.0

*2026-04-17*

### New Features

- **Stream Requests** — New `IStreamRequest<TResponse>` pattern for lazy, item-by-item streaming via `IAsyncEnumerable<TResponse>`. Extends `IRequest<IAsyncEnumerable<TResponse>>` to preserve the unified type hierarchy — `IPipelineBehavior` applies automatically at the request level. New `IStreamPipelineBehavior<TRequest, TResponse>` wraps the actual enumeration for per-item concerns (timing, transforms). Resolved via `IMediator.CreateStream()`. Source generator provides zero-allocation dispatch.
- **Addon Architecture** — New `Outcome` on `IJobContext` (formerly `FailureOutcome`) lets pipeline behaviors control what happens on both success and failure. The worker is a generic state machine that applies the pipeline's decision. Combined with typed metadata and publish pipeline behaviors, this enables building composable addons (retry, mutex, dead letter queue, circuit breaker) entirely on top of Jobly's public API. See [Building Addons](https://github.com/moberghr/jobly/blob/main/docs/guides/building-addons.md).
- **Retry Addon** — Retry logic extracted from the worker into an opt-in module at `Jobly.Core.Retry`. Declare retry policy with `[Retry(3)]` on either the handler or the job class, override per-enqueue with `new JobParameters().WithRetry(maxRetries: 5)`, or set global defaults via `services.AddJoblyRetry(o => { o.MaxRetries = 3; o.Delays = [15, 60, 300]; })`. Priority: per-enqueue metadata > handler attribute > job attribute > global options.
- **Mutex Addon** — Mutex extracted from the worker hot path into an opt-in module at `Jobly.Core.Mutex`. Register via `services.AddJoblyMutex()`. Set keys with `new JobParameters().WithMutex("payment:123")` or `[Mutex("payment-processing")]` on the job class. Uses the new `IJoblyLockProvider` abstraction for distributed locking.
- **Typed Metadata** — Access job metadata through strongly-typed interfaces. Define an interface extending `IJobMetadata`, and read it in handlers via `ctx.GetMetadata<IMyMetadata>()` or configure it at publish time with `new JobParameters().Configure<IMyMetadata>(m => m.CustomerName = "John")`. The source generator produces dictionary-backed implementations. `MetadataSerializer` uses native JSON deserialization for round-trip fidelity (integers stay as `long`, arrays as `List<object>`).
- **Recurring Job Enable/Disable** — Disable a recurring job to temporarily stop it from creating new jobs. The scheduler still fires on schedule but records a "Skipped" entry in the execution history. Re-enabling resumes from the next natural cron occurrence with no catchup burst. API: `POST /api/recurring/{id}/enable|disable`. Dashboard shows Enabled/Disabled badges and Skipped entries in history.
- **Worker Scope Isolation** — Worker and handler now use separate DI scopes. The handler's DbContext lives in its own scope — on failure, the scope is disposed and tracked entities are discarded. No partial handler work leaks into the worker's save. On success, handler changes are committed first (outbox pattern), then Jobly state.
- **Extensible Dashboard UI** — New `IJoblyUIExtension` interface lets external NuGet packages extend the dashboard without forking. Extensions ship an ES-module as an embedded resource, served at `/jobly/_ext/{name}/`. The SPA dynamically imports each module and calls `install(jobly)`. Extensions target `data-jobly-slot` elements with `mount` / `append` / `insertBefore` / `insertAfter` operations, or register whole new pages via `addPage()`. React, ReactDOM, Axios, and shadcn components are exposed on `window.Jobly` so extensions don't bundle them. The built-in `RetryUIExtension` is the reference implementation — renders a retry progress card with attempts/max and next-delay info on the job detail page.

### Improvements

- **Handler Registration Split** — `AddHandlers(assembly)` replaces the old `AddJobHandlers`. New granular methods: `AddJobHandlers` (job + message handlers only), `AddMediatorHandlers` (request + stream handlers only). `AddHandlers` calls both.
- **Dispatcher Split** — `JobDispatcher` (worker job execution) and `MediatorDispatcher` (in-memory request/stream dispatch) are now separate classes with independent method caches.
- **xUnit v3 + Microsoft Testing Platform** — Test suite migrated to xUnit v3 with `UseMicrosoftTestingPlatformRunner`. New `[TimedFact]` / `[TimedTheory]` attributes enforce a 10-second default timeout per test, surfacing deadlocks and hangs globally.
- **Server Memory Benchmarks** — New benchmark project at `src/benchmarks/Jobly.ServerBenchmarks/` with four benchmarks (`ScopeMemoryBenchmark`, `WorkerMemoryBenchmark`, `ServerMemoryBenchmark`, `MemoryStressTest`) and a custom `TotalAllocatedDiagnoser` that tracks allocations across all threads. Baseline: ~50 KB per job regardless of scale; 100K-job stress test shows 0.3 MB retained growth (no leak) at 420–496 jobs/sec steady throughput. Documented in [Operations → Benchmarks](/docs/operations/benchmarks).
- **Mutation Testing** — New `Jobly.Tests.Mutation` project with an in-memory SQLite fixture runs 293 tests in ~10 seconds, enabling a full Core mutation run in ~30 minutes via `dotnet-stryker`. Baseline scores: **Core 99.60%** (743 killed / 3 survived), Worker 51.53%. Fixed a `RecurringJobPublisher` race condition surfaced during mutation analysis — `AddOrUpdateRecurringJob` now uses `IJoblyLockProvider` to prevent duplicate inserts on concurrent calls.
- **.slnx Solution Format** — Migrated from `src/Jobly.sln` to `src/Jobly.slnx` (XML-based solution format).

### Bug Fixes

- **Recurring job race on concurrent update** — `RecurringJobPublisher.AddOrUpdateRecurringJob` could insert duplicate rows when called concurrently with the same name. Now uses `IJoblyLockProvider` for exclusive access during the upsert.
- **Trace page group node highlighting** — Fixed group node highlighting and edge behavior in the trace visualization page.

### Migration

This is a large release with several breaking changes. Plan the upgrade accordingly.

- **Retry is opt-in** — Add `services.AddJoblyRetry()` to enable retries. Without it, failed jobs go directly to `Failed`. Replace the removed `maxRetries` publisher overloads and `JoblyConfiguration.RetryCount` with `[Retry(n)]` attributes, `new JobParameters().WithRetry(n)`, or the global options callback.
- **Mutex is opt-in** — Add `services.AddJoblyMutex()` to enable mutex enforcement. The `JobParameters.Mutex` property is removed — use `.WithMutex("key")` or `[Mutex("key")]` instead. The `ConcurrencyKey` column on the `Job` entity is removed (keys now live in metadata).
- **Typed metadata API** — `IJobContext<T>` / `JobContext<T>` are removed. Read typed metadata via `ctx.GetMetadata<IMyMetadata>()` and configure at publish via `new JobParameters().Configure<IMyMetadata>(m => ...)`.
- **Reduced public surface** — `JobHelper`, `JobDispatcher`, `MetadataSerializer`, and EF interceptors are now `internal`. The Retry and Mutex addons demonstrate that everything needed to build addons is available through the public API — no `InternalsVisibleTo` needed.
- **Database migration required** — New `DisabledAt` column on `RecurringJob`, `Skipped` column on `RecurringJobLog`, `ConcurrencyKey` dropped from `Job`. Run an EF Core migration after upgrading.
- **Solution file renamed** — Update build scripts and IDE shortcuts from `Jobly.sln` to `Jobly.slnx`.

### Stats

- ~770 tests (PostgreSQL + SQL Server) + 293 SQLite mutation tests
- Core mutation score: 99.60% (746 mutants, 743 killed)

---

## 0.6.1

*2026-04-13*

### Bug Fixes

- **Scoped service resolution in lock provider** — `IDistributedLockProvider` singleton factory resolved `DbContextOptions<TContext>` from the root provider, but `AddDbContext` registers it as scoped. This threw `InvalidOperationException` when scope validation is enabled (e.g. `WebApplication.CreateBuilder()` in Development). Fixed by creating a scope inside the factory.

### Code Quality

- **Fix all 401 build warnings** — Zero warnings across the entire solution. Fixes include: regex DoS hardening (NonBacktracking), collection expression simplification, constructor formatting, CancellationToken propagation, string.Equals usage, and async call consistency. All rule suppressions via `.editorconfig` — no `#pragma` or `[SuppressMessage]` attributes.

---

## 0.6.0

*2026-04-12*

### New Features

- **OpenTelemetry Distributed Tracing** — Every job execution creates a `System.Diagnostics.Activity` with W3C-format TraceId, SpanId, and ParentSpanId. Trace context is automatically propagated through job chains: when a handler enqueues a child job, the child's `ParentSpanId` links back to the handler's span. Message routing and batch creation also propagate span context. New `ParentSpanId` column on the Job entity stores the spawner's span ID.
- **Span Attributes** — Job execution spans include OTel semantic convention tags (`messaging.system`, `messaging.destination.name`, `messaging.operation.name`, `messaging.message.id`) and Jobly-specific tags (`jobly.job.type`, `jobly.job.kind`, `jobly.job.status`, `jobly.job.duration_ms`, `jobly.job.retry_count`). Failed spans are marked with `ActivityStatusCode.Error`.
- **Span Events** — Key lifecycle moments recorded as events on the span: `jobly.job.completed`, `jobly.job.failed` (with exception info), `jobly.job.retried` (with retry/max counts), `jobly.job.cancelled`.
- **OTel Metrics** — Four `System.Diagnostics.Metrics` instruments via a `Meter` named `"Jobly"`: `jobly.job.duration` (histogram, ms), `jobly.job.active` (up-down counter), `jobly.job.completed` (counter with status tag), `jobly.job.enqueued` (counter with kind tag). All tagged by queue and type for filtering.
- **Automatic Log Correlation** — `AddJoblyWorker` configures `ActivityTrackingOptions` so TraceId, SpanId, and ParentId appear in log output by default. No additional configuration needed.

### Integration

All features are on by default with zero configuration. To export to OTel backends:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Jobly"))
    .WithMetrics(m => m.AddMeter("Jobly"));
```

### Migration

This release adds a new nullable `ParentSpanId` column to the Job table. Run an EF Core migration after upgrading.

### Bug Fixes

- **Distributed lock credential stripping** — Connection string resolution now reads from `DbContextOptions` RelationalOptionsExtension instead of `Database.GetConnectionString()`, which strips passwords via Npgsql `PersistSecurityInfo=false`. Also handles `NpgsqlDataSource` configurations.
- **Server status indicators** — Dashboard servers page now shows heartbeat-based status dots: green (active), red (stale >30s), amber (paused). "Inactive" badge shown when heartbeat is stale.

### Stats

- 658 tests (310 PostgreSQL + 310 SQL Server + 38 unit)

---

## 0.5.0

*2026-04-09*

### New Features

- **Job Metadata** — Attach key-value metadata to jobs at publish time via `JobParameters.Metadata`. Metadata is inherited by child jobs, accessible in handlers via `IJobContext`, and visible in the dashboard. New `IPublishPipelineBehavior<T>` interface for cross-cutting metadata (e.g., adding tenant ID to every job automatically).
- **Pause / Resume** — Pause and resume job processing at the server or worker group level via dashboard or API. Paused workers stop picking up new jobs; in-progress jobs continue to completion.
- **Real-time Handler Logs** — Handler `ILogger` output is now flushed to the database every ~1 second during execution, instead of only after the handler completes. Logs are visible in the dashboard while the job is still processing.
- **Multi-server Integration Tests** — 16 new tests (8 per database) verify distributed coordination: row locks, advisory locks, orchestration, message routing, and mutex enforcement across two independent servers sharing one database.
- **Deterministic Query Ordering** — Job and message fetch queries now use explicit ordering by queue and schedule time, ensuring predictable behavior in multi-server deployments.
- **Naming Convention Support** — Entity configurations respect EF Core naming conventions (e.g., `UseSnakeCaseNamingConvention()`). All Jobly tables default to the `jobly` schema, configurable via `JoblyConfiguration.Schema`.
- **Configurable Handler Logging** — `EnableHandlerLogging` option (default true) to suppress handler `ILogger` output from the JobLog table when not needed. Lifecycle events are always recorded.
- **AI-friendly Documentation** — Added `llms.txt` and `llms-full.txt` for LLM/agent consumption, following the llms.txt convention.

### Improvements

- Sidebar reorganized into logical groups: Patterns, Features, Operations, Dashboard
- Dashboard shows metadata alongside job payload as formatted JSON
- NuGet badges added to README
- Deterministic query ordering for predictable multi-server behavior

### Stats

- 632 tests (316 PostgreSQL + 316 SQL Server)

---

## 0.4.0

*2026-04-08*

### New Features

- **Source Generator** — Zero-allocation mediator and worker dispatch via compile-time source generation. Replaces runtime reflection in `JobDispatcher` for handler discovery and execution.

### Links

- [GitHub Release](https://github.com/moberghr/jobly/releases/tag/0.4.0)

---

## 0.3.0

*2026-04-07*

### New Features

- Initial public release with core job processing, message queue, in-memory mediator, dashboard, recurring jobs, batches, cancellation, mutex, crash recovery, and tracing.

### Links

- [GitHub Release](https://github.com/moberghr/jobly/releases/tag/0.3.0)
