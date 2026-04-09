---
sidebar_position: 6
---

# Releases

## 0.5.0

*2026-04-09*

### New Features

- **Job Metadata** — Attach key-value metadata to jobs at publish time via `JobParameters.Metadata`. Metadata is inherited by child jobs, accessible in handlers via `IJobContext`, and visible in the dashboard. New `IPublishPipelineBehavior<T>` interface for cross-cutting metadata (e.g., adding tenant ID to every job automatically).
- **Pause / Resume** — Pause and resume job processing at the server or worker group level via dashboard or API. Paused workers stop picking up new jobs; in-progress jobs continue to completion.
- **Real-time Handler Logs** — Handler `ILogger` output is now flushed to the database every ~1 second during execution, instead of only after the handler completes. Logs are visible in the dashboard while the job is still processing.
- **Multi-server Integration Tests** — 16 new tests (8 per database) verify distributed coordination: row locks, advisory locks, orchestration, message routing, and mutex enforcement across two independent servers sharing one database.
- **Deterministic Query Ordering** — Job and message fetch queries now use explicit ordering by queue and schedule time, ensuring predictable behavior in multi-server deployments.
- **AI-friendly Documentation** — Added `llms.txt` and `llms-full.txt` for LLM/agent consumption, following the llms.txt convention.

### Improvements

- Sidebar reorganized into logical groups: Patterns, Features, Operations, Dashboard
- Dashboard shows metadata alongside job payload as formatted JSON
- NuGet badges added to README

### Stats

- 608 tests (304 PostgreSQL + 304 SQL Server)
- 5 PRs merged (#90-#95)

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
