# Spec — Job Progress Reporting

**Status:** Draft, awaiting approval
**Date:** 2026-05-12
**Branch:** `feat/progress`

## Goal

Let handlers report named progress bars (`0..100`) that the dashboard displays per job, decoupled from job state. Backed by JobLog rows of a new `Progress` event type; no schema changes to `Job`.

## Design (agreed in conversation)

- API on `IJobContext`: `void ReportProgress(string name, int percent)` and `void ReportProgress(int percent)` overload (empty name = single-bar case). Percent clamped to `0..100`.
- New in-memory `JobProgressCollector` (internal), one per running job, mirrors `JobLogCollector` plumbing. Holds `ConcurrentDictionary<string, int>` of current values + last-drained snapshot for dedup.
- `JobLog` gets two new nullable columns: `Name` (string, max 100) and `Value` (`short?`, 0..100). Null on every existing event type. Set only when `EventType == "Progress"`.
- `RunJobMonitor` (already loops every `min(LogFlushInterval, CancellationCheckInterval)`) drains the progress collector alongside the log collector. Each changed bar emits one `JobLog` row; unchanged bars emit nothing. Final drain happens on the worker's terminal `SaveJobLogs` calls in the success / cancellation / failure branches.
- Index on `JobLog`: existing `(JobId)` index is sufficient. The detail page query is `WHERE JobId = ? ORDER BY Timestamp` over a single job's rows, which the `JobId` index covers cheaply. No composite index added.
- Job state machine, worker hot path, orchestrator: untouched.

## Public Contracts

- New: `IJobContext.ReportProgress(string name, int percent)`
- New: `IJobContext.ReportProgress(int percent)` (overload, name = `""`)
- New: `JobLog.Name` (string?, max 100)
- New: `JobLog.Value` (short?)
- New: `JobLogModel.Name`, `JobLogModel.Value` (projected through `GetJobDetailById`)
- New string constant `"Progress"` for `JobLog.EventType` (EventType is a `string` column, no enum to extend)
- No new API endpoints, no list-view surface — progress is read from the existing `GetJobDetailById` response and rendered only on the detail page.

## Change Manifest

**Core (new/modified):**

1. `src/core/Warp.Core/Handlers/IJobContext.cs` — add `ReportProgress` methods to interface + `JobContext`
2. `src/core/Warp.Core/Logging/JobProgressCollector.cs` — NEW, internal collector
3. `src/core/Warp.Core/Data/Entities/JobLog.cs` — add `Name`, `Value` properties
4. `src/core/Warp.Core/ServiceConfiguration.cs` — extend `AddJobLogEntity` with new properties (no new index)
5. `src/core/Warp.Worker/WarpWorkerService.cs` — create progress collector, wire into `JobContext`, drain in `RunJobMonitor`, final drain on terminal paths
6. `src/core/Warp.Worker/WarpDispatcherWorker.cs` — same wiring on the dispatcher path (`CollectLogs` extended to drain progress)

**Query layer:**

7. `src/core/Warp.Core/Models/JobLogModel.cs` — add `Name`, `Value` properties
8. `src/core/Warp.Core/Services/JobQueryService.cs` — project new columns in `GetJobDetailById`

**UI React:**

9. `src/ui/src/types/index.ts` — extend `JobLogModel` with `name`, `value`
10. `src/ui/src/pages/detail/DetailPage.tsx` — render per-bar progress bars in the right column above History/Logs when present; exclude `Progress` rows from the history feed

**Tests:**

11. `src/tests/Warp.Tests/Observability/JobProgressCollectorTests.cs` — NEW, unit (NoDb): dedup, clamping, drain shape
12. `src/tests/Warp.Tests/Observability/JobContextProgressTests.cs` — NEW, unit (NoDb): `ReportProgress` writes into collector, clamping
13. `src/tests/Warp.Tests/Worker/ProgressReportingIntegrationTests.cs` — NEW, integration (PG + SQL Server): single-worker mode end-to-end + dedup
14. `src/tests/Warp.Tests/Worker/ProgressReportingDispatcherIntegrationTests.cs` — NEW, integration via `WarpTestServer` with `UseDispatcher = true`: covers the dispatcher's batched-completion `CollectLogs` path
15. `src/tests/Warp.Tests/Features/Cancellation/CancellationProgressIntegrationTests.cs` — NEW, integration: progress reported before cancellation survives in DB
16. `src/tests/Warp.Tests/Admin/JobQueryProgressTests.cs` — NEW, unit (PG + SQL Server): `GetJobDetailById` projects `Name`/`Value` for `Progress` rows
17. `src/tests/Warp.Tests/TestData/Handlers/ProgressReportingCommand.cs` — NEW, test handlers (single-bar, multi-bar, dedup, no-progress, cancellable-with-progress)

**Out of scope (removed during review):**

- `POST /api/jobs/progress` endpoint and `IJobQueryService.GetLatestProgressForJobs` — the list-view consumer was deemed not worth the column noise; feature scoped to detail page only.
- `JobProgressMap` TypeScript type and `getJobsProgress` API client.
- Progress column in `JobListPage` / `FilteredJobsTable`.

## Implementation Batches

**Batch 1 — Schema + entity**
- `JobLog.Name`, `JobLog.Value` properties
- `AddJobLogEntity` updated with property registration (no new index — existing `(JobId)` suffices for detail-page queries)
- Checkpoint: `dotnet build Warp.slnx`, NoDb tests pass

**Batch 2 — Collector + IJobContext API + worker drain**
- `JobProgressCollector.cs`
- `IJobContext.ReportProgress` + `JobContext` impl, exposes `ProgressCollector` field for the worker to set
- `WarpWorkerService` and `WarpDispatcherWorker`: instantiate collector, assign to `JobContext`, drain in `RunJobMonitor`, final drain in terminal branches (success / cancel / fail)
- Unit tests for collector (dedup, clamp) and `JobContext.ReportProgress` (writes into collector)
- Checkpoint: NoDb tests pass

**Batch 3 — Integration tests**
- Single-worker end-to-end + dedup
- Dispatcher mode via `WarpTestServer.StartAsync(... UseDispatcher = true ...)` exercises `CollectLogs` path
- Cancellation mid-flight — progress reported before cancellation survives
- Checkpoint: PostgreSql + SqlServer test categories pass

**Batch 4 — Query projection**
- Project `Name`/`Value` in `GetJobDetailById`'s log projection
- Unit test asserting the projection
- Checkpoint: NoDb + PG + MSSQL tests pass

**Batch 5 — UI**
- DetailPage renders stacked named bars in the right column above History/Logs when present; exclude Progress rows from the system-event history feed
- Manual smoke via `npm run dev`
- Checkpoint: `npm run build`

**Removed during review:** original Batch 4 (list-view query method, `POST /api/jobs/progress`) and original Batch 6 list-view column. Detail-page-only scope.

## Assumptions

- `JobLog.EventType` stays a string column; we use `"Progress"` literal alongside the existing string values. No enum.
- Tests reset DB via Respawn — no migration scripts needed for the codebase; users on existing deployments will need to run their own EF migration to add the two nullable columns. No new index. Documented in release notes, not code.
- Handler concurrency: handlers can call `ReportProgress` from multiple threads. `ConcurrentDictionary` handles that. No transactional ordering across bar names is promised (last-write-wins per bar, which is what users expect).
- `Name = ""` is a valid bar name (single-bar case). Empty-string keys work fine in `ConcurrentDictionary`.
- `Name` max length 100 — string column. Anything past 100 chars gets truncated by the DB; we don't enforce in code.

## Risks / Open

- **Final drain ordering**: terminal drain must happen in the same `SaveChanges` as the worker's final state writes, otherwise on a worker crash mid-drain progress rows could be orphaned. The existing `SaveJobLogs` already AddRange's into the same context — we slot in alongside. (Mitigated.)
- **List-view query cost on hot dashboards**: for 50 jobs × 3 bars × thousands of rows each, the `GroupBy + OrderByDescending + First` LINQ pattern needs an index hit. Composite `(JobId, EventType, Name, Timestamp DESC)` covers it. We verify with EXPLAIN during integration testing on PG.
- **EF Core LINQ translation**: `g.OrderByDescending(x => x.Timestamp).First()` inside `GroupBy.Select` — confirmed translatable on EF Core 10 for both providers (uses lateral / cross apply). If translation fails on one provider, fall back to two-step fetch (still pure LINQ, no raw SQL §5.1).
- **UI density**: list-view progress column might be visually noisy. Hide-when-empty mitigates. Acceptable.

## Out of Scope

- Progress history curve in the UI (the data is available; rendering deferred to a follow-up).
- Streaming progress updates over SignalR / SSE (poll-based UI is fine for v1).
- Per-bar status colors or pause/error states (just a percent for now).
- Setting expected duration or ETA (`percent + extrapolation` is enough).
- Progress for batches / messages (their children's individual progress is not aggregated — batch progress bar already exists from green/red children counts).

## Security Impact

None. Display telemetry only; no auth/permissions/PII concerns beyond what existing JobLog already carries.

## Scope Classification

Feature (multi-file, library + UI), no breaking changes, low risk.
