# Jobly Roadmap

Feature ideas for future development.

## Concurrency & Flow Control

### Job Timeout
Kill jobs running longer than a configured duration. Leverages existing CancellationMode — worker sets a timer, triggers Graceful cancellation on timeout.

### Mutexes
Only one job of a given type (or resource key) running at a time. Other jobs wait or get rescheduled.

### Semaphores
Max N concurrent jobs per type/resource. E.g., "max 20 newsletter send jobs at once." Jobs beyond the limit wait.

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
