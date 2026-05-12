# Plan: Job Timeout

Spec: `docs/specs/2026-05-12-job-timeout.md`.

## Change manifest

New files (`src/core/Warp.Core/Timeout/`):

- `TimeoutAttribute.cs`
- `TimeoutMode.cs`
- `TimeoutScope.cs`
- `ITimeoutMetadata.cs`
- `TimeoutOptions.cs`
- `TimeoutExtensions.cs`
- `TimeoutPublishBehavior.cs`
- `TimeoutPipelineBehavior.cs`
- `TimeoutServiceConfiguration.cs`

New test files (`src/tests/Warp.Tests/Features/Timeout/`):

- `TimeoutAttributeTests.cs` (NoDb)
- `TimeoutTests.cs` (unit against real DB, NoDb-eligible parts where possible)
- `TimeoutIntegrationTests.cs` (E2E)

Modifications:

- `CLAUDE.md` — add `opt.AddTimeout()` to the opt-in addon list (single line).
- `ROADMAP.md` — strike out the Job Timeout entry.

New docs:

- `website/docs/features/timeout.md`

## Test manifest

NoDb:
1. `TimeoutAttribute_NonPositive_Throws` — `new TimeoutAttribute(0)` / `-1` throws `ArgumentOutOfRangeException`.
2. `WithTimeout_NonPositive_Throws` — `WithTimeout(TimeSpan.Zero)` and negative throw.
3. `WithTimeout_SetsTimeoutSecondsInMetadata` — round-trip on a `JobParameters`.

PostgreSQL + SQL Server (unit-style, real DB):
4. `PublishBehavior_AppliesAttribute_WhenMetadataMissing` — publish a job, fetch row, metadata has `TimeoutSeconds`.
5. `PublishBehavior_WithTimeoutWinsOverAttribute` — extension preserves explicit override.
6. `PublishBehavior_DefaultAppliedWhenNoAttribute` — `o.Default = 5s` → metadata `TimeoutSeconds = 5`.
7. `PipelineBehavior_NoTimeoutMetadata_PassesThrough` — no `next` interference.
8. `PipelineBehavior_HandlerHonoursToken_OutcomeDeletedWithMessage` — handler awaits cancellable Task.Delay → outcome `Deleted`, log "Timed out after Xs".
9. `PipelineBehavior_WorkerShutdownPropagates_NoTimeoutOutcome` — pass a cancelled outer token; OCE propagates; outcome stays null.

PostgreSQL + SQL Server (integration):
10. `DeleteMode_JobExceedsTimeout_EndsDeleted_WithTimeoutLog` — `[Timeout(1)]` (default Delete), handler `Task.Delay(5s, ct)`. `WaitForJobState(Deleted)`. Audit "Timed out after 1s".
11. `DeleteMode_PlusAddRetry_TimedOutNotRetried` — `AddRetry(MaxRetries=2)` + `[Timeout(1)]`. Job ends `Deleted` after one attempt.
12. `FailMode_NoRetry_EndsFailed_WithTimeoutException` — `[Timeout(1, Mode = Fail)]`, no `AddRetry`. Job ends `Failed`, exception message contains "timed out after 1s".
13. `FailMode_PlusAddRetry_PerAttempt_IsRetried_ThenFails` — `AddRetry(MaxRetries=2)` + `[Timeout(1, Mode = Fail)]` (PerAttempt). Three attempts (1 + 2 retries), each times out. Final state `Failed`.
14. `FailMode_PlusAddRetry_TotalScope_BoundsTotalWallClock` — `AddRetry(MaxRetries=5, Delays=[100ms])` + `[Timeout(2, Mode = Fail, Scope = Total)]`. Handler sleeps past deadline on every attempt. Assert: terminal state `Failed`, total wall-clock from CreateTime to terminal log ≤ ~2.5s (deadline + delay slack), retry count < `MaxRetries + 1`.
15. `TimeoutCounter_Incremented` — assert `stats:timeout` row exists with the expected count after a few timed-out jobs.

(Numbers are spec-driven; actual file split may merge cases.)

## Implementation batches

### Batch 1: Core addon
Files: all 9 new `Warp.Core/Timeout/` files. Build green. Use `TimeProvider.CreateCancellationTokenSource(delay)` instead of `CancellationTokenSource.CancelAfter` for FakeTimeProvider testability. Inject `TimeProvider` into both behaviors. Decide the counter-write seam during this batch: if `IJobContext` exposes a counter sink, use it; otherwise add a small `ICounterRecorder` service with a per-DbContext implementation (a few lines, mirrors how `FinalizeJobState` writes counters).

Checkpoint: `dotnet build src/Warp.slnx` → 0 warnings.

### Batch 2: NoDb tests
`TimeoutAttributeTests.cs`. Three pure-CLR tests (no DB).

Checkpoint: `dotnet test --project tests/Warp.Tests/Warp.Tests.csproj -- --filter-trait "Category=NoDb"` green.

### Batch 3: Pipeline & publish behavior unit tests
`TimeoutTests.cs` — abstract base + `[GenerateDatabaseTests]`. Tests 4–9. Uses a `FakeTimeProvider` shared with the pipeline behavior so cancellation triggers can be driven without real waits.

Checkpoint: `dotnet test --project tests/Warp.Tests/Warp.Tests.csproj` with the Timeout namespace filter → green on PG + SQL Server.

### Batch 4: Integration tests
`TimeoutIntegrationTests.cs` — `[GenerateDatabaseTests(FixtureKind.Integration)]`. Tests 10–12. Real wall-clock timeouts (short — 200–500 ms) since `WarpTestServer` uses real worker threads.

Checkpoint: `dotnet test` namespace filter → green on both backends.

### Batch 5: Docs + ROADMAP + CLAUDE.md
Add docs page, strike roadmap entry, append addon line to `CLAUDE.md`.

Checkpoint: full `dotnet test` green; manual diff review.

## Risk register

- **`TimeProvider.CreateCancellationTokenSource` not present on net10.0** — verified: ships on .NET 8+; available. If not behaving as expected, fall back to `CancellationTokenSource.CancelAfter` and use real-wall-clock tests (slower, still passes).
- **Pipeline ordering vs retry** — covered in spec. Confirmed by tests 11/12.
- **Handler-ignores-token path** — same risk as today's `DeleteJob`; documented, no extra mitigation needed.

## Approval gate hand-off

Spec: `docs/specs/2026-05-12-job-timeout.md`.
Plan: `docs/plans/2026-05-12-job-timeout.md`.
Todo: `tasks/todo.md`.
Scope: small feature. Inline implementation path. ~9 new files of code + ~3 modifications + 1 new docs page.
