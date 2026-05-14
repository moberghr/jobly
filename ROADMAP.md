# Warp Roadmap

Feature ideas for future development.

## Concurrency & Flow Control

### ~~Job Timeout~~ ✅
Implemented as the `opt.AddTimeout()` addon in `Warp.Core.Timeout`. `[Timeout(seconds: N, Mode, Scope)]` attribute and `WithTimeout(TimeSpan, TimeoutMode, TimeoutScope)` extension on `JobParameters`. Two modes: `Delete` (outcome → `Deleted`, not retried) and `Fail` (throws `TimeoutException`, retried by `AddRetry`). Two scopes: `PerAttempt` and `Total` (deadline anchored at `CreateTime`, bounds the full retry chain). Pipeline behaviour wraps the handler with `new CancellationTokenSource(delay, TimeProvider)` for `FakeTimeProvider` testability. `AddRetry()` must be registered before `AddTimeout()` so retry can catch Fail-mode's `TimeoutException`. Cooperative cancellation only — same constraint as `DeleteJob`. `stats:timeout` counter deferred to v1.1.

### ~~Mutexes~~ ✅
Implemented via `ConcurrencyKey` on Job entity. Set at publish time via `JobParameters.Mutex`. Worker cancels duplicate if another job with same key is already Processing.

### Semaphores
Max N concurrent jobs per type/resource. E.g., "max 20 newsletter send jobs at once." Uses same `ConcurrencyKey` column — different enforcement logic (count instead of exists check).

### Rate Limiting
Max N executions per time window per type. Fixed window (100/minute) or sliding window. Throttled jobs rescheduled to later.

### Unique Jobs
Don't enqueue if an identical job is already pending (Enqueued/Processing). Dedup by type + serialized payload hash.

### Job Priority
Explicit priority levels within a queue. Higher priority jobs fetched first.

### ~~Pause/Resume~~ ✅
Implemented at the server and worker group level. Paused servers/groups stop picking up new jobs; in-progress jobs continue to completion. Controllable via dashboard or API.

## Observability

### Job Progress Reporting
Handlers report 0-100% progress via a context object. Stored on the job, visible in dashboard with progress bar.

### OpenTelemetry Integration
Activity/span for job execution. Correlates with HTTP traces via TraceId. Export to Jaeger/Zipkin/etc.

### ~~Job Metadata~~ ✅
Implemented as `JobParameters.Metadata` (key-value pairs), `IJobContext` for handler access, and `IPublishPipelineBehavior<T>` for cross-cutting metadata. Metadata inherited by child jobs. Visible in dashboard.

### Webhook on Completion
Configure a URL to POST to when a job reaches a terminal state. Useful for async API patterns.

## Performance & Compilation

### Native AOT Support
Make Warp compatible with Native AOT compilation. Replace reflection-based handler discovery (JobDispatcher) with source generators. Eliminates startup cost and enables trimming.

### Source Generators
Generate handler registration, type mappings, and serialization code at compile time. Replaces runtime reflection in JobDispatcher (DiscoverJobHandler, DiscoverMessageHandlers, ExecuteHandler). Enables AOT and improves startup performance.

## Infrastructure

### ~~Database Migrations~~ ✅
Warp's entities are added to the user's DbContext model via `WarpModelCustomizer`. Standard EF Core migrations (`dotnet ef migrations add`) pick up Warp's tables automatically, including on NuGet upgrades. Documented in Getting Started.

### ~~In-Memory Mediator~~ ✅
Implemented as `IRequest<TResponse>` with `IMediator.Send()`. Supports `IPipelineBehavior<TRequest, TResponse>` for cross-cutting concerns. Same pipeline as jobs and messages, no database persistence.

### ~~Stream Requests~~ ✅
Implemented as `IStreamRequest<TResponse>` extending `IRequest<IAsyncEnumerable<TResponse>>`. Preserves the unified type hierarchy — `IPipelineBehavior` applies at request level. `IStreamPipelineBehavior<TRequest, TResponse>` wraps enumeration. Source generator provides zero-allocation dispatch.

### ~~HTTP Endpoint Exposure~~ ✅
Implemented as the optional `Moberg.Warp.Http` package. `[WarpHttpGet/Post/Put/Patch/Delete("/route")]` on a handler class exposes its `IRequest<T>` / `IStreamRequest<T>` request type as an ASP.NET Minimal API endpoint. Source-generated dispatch; binding delegated to ASP.NET Minimal API (full `IParsable<T>` / `TryParse` / route constraints / query arrays). `IJob` and `IMessage` rejected at compile time (`WHTTP001`); use a thin `IRequest<Guid>` wrapper that calls `IPublisher.Enqueue` for "submit a job via HTTP". Independent of `Moberg.Warp.UI`.

### Runtime Schema Migration Helper
Optional `MigrateWarpSchemaAsync()` for users who don't use EF migrations. Diffs the EF model against the database at runtime, generates and executes only Warp table DDL. Respects naming conventions. Lower priority — EF migrations cover most users.
