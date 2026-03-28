---
sidebar_position: 2
---

# Jobs

Browse jobs by state using the left sidebar: Enqueued, Scheduled, Processing, Completed, Failed, Awaiting.

Each state shows a count. Bulk requeue or delete with checkboxes.

![Failed Jobs](/img/screenshots/02-jobs-failed.png)

## Job Detail

Click any job to see its full detail in a two-column layout:

**Left column:**
- **Payload** — The serialized job data
- **Details** — Type, handler, timestamps, retry count
- **Flow** — Trace ID, spawned-by link, message link, continuation link
- **Trace** — All jobs sharing the same TraceId (click to navigate)
- **Sibling Jobs** — Other jobs from the same message
- **Child Jobs** — Continuation jobs waiting on this one

**Right column:**
- **History** — Colored state cards (Created → Processing → Completed/Failed) with timestamps and durations
- **Handler Output** — Pipeline behavior logs and ILogger output captured during execution
- **Exception** — Full stack trace on failed jobs

### Completed Job with Trace

![Job Detail with Trace](/img/screenshots/03-job-detail-trace.png)

### Failed Job with Exception

![Failed Job Detail](/img/screenshots/09-job-detail-failed.png)
