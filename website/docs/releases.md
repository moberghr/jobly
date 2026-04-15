---
sidebar_position: 6
---

# Releases

## 0.7.0

*2026-04-15*

### New Features

- **Stream Requests** — New `IStreamRequest<TResponse>` pattern for lazy, item-by-item streaming via `IAsyncEnumerable<TResponse>`. Extends `IRequest<IAsyncEnumerable<TResponse>>` to preserve the unified type hierarchy — `IPipelineBehavior` applies automatically at the request level. New `IStreamPipelineBehavior<TRequest, TResponse>` wraps the actual enumeration for per-item concerns (timing, transforms). Resolved via `IMediator.CreateStream()`. Source generator provides zero-allocation dispatch.
- **Retry as Pipeline Module** — Retry logic extracted from the worker into a composable pipeline module. Register with `services.AddJoblyRetry(o => { o.MaxRetries = 3; o.Delays = [15, 60, 300]; })`. Built on the new addon architecture: `RetryPublishBehavior` injects retry config into metadata at publish time, `RetryPipelineBehavior` catches handler failures and sets `FailureOutcome` to re-enqueue with backoff. The worker applies whatever the pipeline decides — it has zero retry knowledge. See [Building Addons](https://github.com/moberghr/jobly/blob/main/docs/guides/building-addons.md) for the full architecture.
- **Typed Metadata** — Job metadata (`Dictionary<string, object>`) can now be accessed via strongly-typed interfaces. Define an interface extending `IJobMetadata` with typed properties — the source generator produces a dictionary-backed implementation. Inject `IJobContext<IMyMetadata>` in handlers for typed access. `MetadataSerializer` uses native JSON deserialization for round-trip fidelity (integers stay as `long`, arrays as `List<object>`).
- **Recurring Job Enable/Disable** — Disable a recurring job to temporarily stop it from creating new jobs. The scheduler still fires on schedule but records a "Skipped" entry in the execution history. Re-enabling resumes from the next natural cron occurrence with no catchup burst. API: `POST /api/recurring/{id}/enable|disable`. Dashboard shows Enabled/Disabled badges and Skipped entries in history.
- **Worker Scope Isolation** — Worker and handler now use separate DI scopes. The handler's DbContext lives in its own scope — on failure, the scope is disposed and tracked entities are discarded. No partial handler work leaks into the worker's save. On success, handler changes are committed first (outbox pattern), then Jobly state.
- **Addon Architecture** — New `JobFailureOutcome` and `IJobContext.FailureOutcome` allow pipeline behaviors to control what happens when a handler fails. The worker is a generic state machine that applies the pipeline's decision. Combined with typed metadata and publish pipeline behaviors, this enables building composable addons (retry, dead letter queue, circuit breaker) without modifying Jobly core.

### Improvements

- **Handler Registration Split** — `AddHandlers(assembly)` replaces the old `AddJobHandlers`. New granular methods: `AddJobHandlers` (job + message handlers only), `AddMediatorHandlers` (request + stream handlers only). `AddHandlers` calls both.
- **Dispatcher Split** — `JobDispatcher` (worker job execution) and `MediatorDispatcher` (in-memory request/stream dispatch) are now separate classes with independent method caches.
- **xUnit v3 + Microsoft Testing Platform** — Test suite migrated to xUnit v3 with `UseMicrosoftTestingPlatformRunner`.

### Bug Fixes

- **Trace page group node highlighting** — Fixed group node highlighting and edge behavior in the trace visualization page.

### Migration

- **Retry module opt-in** — Retry is no longer built into the worker. Add `services.AddJoblyRetry()` to opt in. Without it, failed jobs go directly to `Failed` state with no retries. The `publisher.Enqueue(job, maxRetries: 3)` API still works — the retry module reads from both the entity column and metadata.
- **Database migration required** — New `DisabledAt` column on `RecurringJob` and `Skipped` column on `RecurringJobLog`. Run an EF Core migration after upgrading.

### Stats

- 764 tests (382 PostgreSQL + 382 SQL Server)

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
