---
sidebar_position: 6
---

# Releases

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
