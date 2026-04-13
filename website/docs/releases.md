---
sidebar_position: 6
---

# Releases

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
