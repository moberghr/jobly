# Jobly Roadmap

Feature ideas for future development.

## Concurrency & Flow Control

### Job Timeout
Kill jobs running longer than a configured duration. Leverages existing CancellationMode — worker sets a timer, triggers Graceful cancellation on timeout.

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

### Pause/Resume Queues
Stop processing a specific queue without stopping the server. Workers skip paused queues.

## Observability

### Job Progress Reporting
Handlers report 0-100% progress via a context object. Stored on the job, visible in dashboard with progress bar.

### OpenTelemetry Integration
Activity/span for job execution. Correlates with HTTP traces via TraceId. Export to Jaeger/Zipkin/etc.

### Job Tags / Metadata
Arbitrary key-value pairs on jobs. Filterable in dashboard. Useful for grouping by tenant, feature area, etc.

### Webhook on Completion
Configure a URL to POST to when a job reaches a terminal state. Useful for async API patterns.

## Performance & Compilation

### Native AOT Support
Make Jobly compatible with Native AOT compilation. Replace reflection-based handler discovery (JobDispatcher) with source generators. Eliminates startup cost and enables trimming.

### Source Generators
Generate handler registration, type mappings, and serialization code at compile time. Replaces runtime reflection in JobDispatcher (DiscoverJobHandler, DiscoverMessageHandlers, ExecuteHandler). Enables AOT and improves startup performance.

## Infrastructure

### Database Migrations
Hybrid approach: ship SQL migration scripts per version in the NuGet package AND provide `MigrateJoblySchema()` helper that runs them automatically at startup. Currently uses `EnsureCreatedAsync()` which only works for fresh databases. Need versioned schema tracking (migration table), per-provider SQL scripts (PostgreSQL + SQL Server), and idempotent migrations.

## In-Process Messaging

### In-Memory Mediator
Use Jobly's handler interfaces (IJobHandler, IMessageHandler, IPipelineBehavior) for in-process request/response and pub/sub without database persistence. Same handler code works for both in-memory (immediate) and background (persisted) execution. Lets you use Jobly as the single processing abstraction in your app — like MediatR but with the option to go async/persisted when needed.
