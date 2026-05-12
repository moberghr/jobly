# Plan — Job Progress Reporting

**Spec:** `docs/specs/2026-05-12-job-progress-reporting.md`

## Approach Summary

Hook into the existing `RunJobMonitor` drain loop in `WarpWorkerService` (and `WarpDispatcherWorker`'s parallel `RunJobMonitor` + `CollectLogs`) so progress reporting reuses the same plumbing as real-time log flushing. New collector (`JobProgressCollector`) is symmetric with `JobLogCollector`: in-memory dictionary with dedup-on-no-change, drained on each tick into `JobLog` rows tagged with `EventType = "Progress"`. Two new nullable columns on `JobLog` (`Name`, `Value`) carry the per-bar payload. Job state machine untouched; worker fetch path untouched. Detail-page-only UI surface — no list-view column, no list-view endpoint.

## Files Touched (Change Manifest)

See spec §Change Manifest. Backend: 6 files + 1 new collector. Query/UI: 3 files. Tests: 6 files (5 test files + 1 test-handler file).

## Test Plan

### Unit (NoDb)

- `JobProgressCollectorTests`
  - `Drain_NewBar_EmitsOneRow`
  - `Drain_SameValueTwice_EmitsOneRowThenNone`
  - `Drain_ChangedValue_EmitsRow`
  - `Drain_MultipleNamedBars_EmitsOneRowPerBar`
  - `Drain_NoReports_ReturnsEmpty`
  - `Drain_OnlyChangedBars_EmitsRow_OthersSkipped`
  - `Drain_PopulatesJobIdAndWorkerId`

- `JobContextProgressTests`
  - `ReportProgress_WithoutCollector_DoesNotThrow` (defensive — `ProgressCollector` null)
  - `ReportProgress_WithCollector_WritesToCollector`
  - `ReportProgress_NoName_UsesEmptyString`
  - `ReportProgress_NegativePercent_ClampsToZero`
  - `ReportProgress_OverHundred_ClampsToOneHundred`
  - `ReportProgress_NullName_TreatedAsEmptyString`

### Unit (PG + SQL Server, via `[GenerateDatabaseTests]`)

- `JobQueryProgressTests`
  - `GetJobDetailById_ProgressRows_ProjectedWithNameAndValue`

### Integration (PG + SQL Server, via `[GenerateDatabaseTests]` against `WarpWorkerService` directly)

- `ProgressReportingIntegrationTests`
  - `Handler_ReportsProgress_WrittenAsJobLogRows`
  - `Handler_ReportsMultipleNamedBars_AllPersisted`
  - `Handler_DoesNotReportProgress_NoProgressRowsCreated`
  - `Handler_ReportsSameValueRepeatedly_WritesAtMostOneRowPerBar`
  - `Handler_ReportsProgress_LeavesOtherEventTypesNameAndValueNull`

### Integration (PG + SQL Server, via `WarpTestServer`)

- `ProgressReportingDispatcherIntegrationTests`
  - `GivenDispatcherMode_WhenHandlerReportsProgress_ThenProgressRowsArePersistedViaBatchedCompletion` — `UseDispatcher=true` so the batched-completion `CollectLogs` path runs

- `CancellationProgressIntegrationTests`
  - `GivenHandlerThatReportsProgressThenIsCancelled_WhenCancelled_ThenReportedProgressRowsSurvive`

## Checkpoints

After each batch:
- `dotnet build Warp.slnx` — zero warnings (StyleCop / Roslynator enforced)
- For Batch 1 / 2: `dotnet test --project tests/Warp.Tests/Warp.Tests.csproj -- --filter-trait "Category=NoDb"`
- For Batch 3+: targeted PG / SQL Server runs by class filter
- For Batch 5: `npm run build` in `src/ui/`

After all batches:
- Full suite: `dotnet test --project tests/Warp.Tests/Warp.Tests.csproj`
- `git diff --stat` to confirm only manifest files touched

## Risks

- Stale-flake on PostgreSQL fixture init is a known infra issue (`memory: project_flaky_tests.md`) — retry once before investigating if a fresh-fixture test fails on first run.
- Schema change is additive (two nullable columns, no new index). No data migration required. Users on existing deployments need to add the two columns via their normal EF migration step.
