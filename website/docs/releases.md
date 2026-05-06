---
sidebar_position: 6
---

# Releases

## 0.11.0

*2026-05-06*

Stability release. No breaking API changes. Several latent product bugs surfaced as flakes during a test-infrastructure refactor and are fixed here; pause's "not instant" semantics are now spelled out in the public API surface.

### Improvements

- **Pause is documented as heartbeat-driven** — `IServerCommandService.PauseServer` / `PauseWorkerGroup` only stamp `PausedAt` on the DB row. Each server's worker pool keeps fetching until that server's next `Heartbeat` tick (cadence `HealthCheckInterval`, default 3s) refreshes its in-memory `PauseStateHolder`, and an in-flight worker iteration that already passed its pause check will still complete its current claim. Treat pause as "no new fetches after up to one heartbeat", not as a synchronous barrier — callers needing hard quiesce semantics combine pause with a wait of `HealthCheckInterval + PollingInterval`. The previous docs implicitly suggested instant propagation; the type docs now match the actual behavior.
- **`HealthCheckInterval` is now nullable** — `WarpWorkerConfiguration.HealthCheckInterval` is `TimeSpan?`. Set it to `null` to disable the auto `Heartbeat` loop entirely (the task stays DI-resolvable for manual invocation). Useful for tests that need to drive heartbeat ticks deterministically; existing values continue to work unchanged.
- **MIT LICENSE file** — repository root now ships a proper `LICENSE` file alongside the `MIT` `PackageLicenseExpression` already present in NuGet metadata, and the `README` has a license section pointing at it ([#149](https://github.com/moberghr/warp/pull/149)).

### Bug Fixes

- **Dispatcher shutdown is now exception-safe** — `WarpDispatcher.ExecuteAsync` wraps its loop in `try/finally` so the channel writer is always completed and the per-host registration disposed, even when an unexpected exception escapes the loop body. Without this, a non-`OperationCanceledException` slipping through the catch could leave dispatcher workers blocked on `WaitToReadAsync` until `IHostOptions.ShutdownTimeout` (default 30s) fired, masking the real failure. Closes the `DispatcherShutdownIntegrationTests` flake from [#151](https://github.com/moberghr/warp/pull/151).
- **`WarpServerRegistration.StopAsync` cleanup uses a fresh CTS** — the cancellation token passed to `StopAsync` is often already cancelled by the time host shutdown reaches the registration's stop method; reusing it left the cleanup queries (delete `Worker` / `WorkerGroup` / `Server` rows for this server id) in `(canceled)` state, leaving orphan rows for `ServerCleanup` to find on the next host's tick. Now uses a bounded fresh `CancellationTokenSource` (10s) so graceful cleanup completes regardless of upstream cancellation.
- **`ServerTaskLoop` survives transient SQL hiccups** — `EnsureRegisteredAsync` and the main iteration are now wrapped in `try/catch` with a short backoff. Previously a transient `SqlException` (e.g., a "session busy" / "severe error" under load) escaping these methods would crash the whole `ServerTaskHost` because `BackgroundServiceExceptionBehavior=StopHost` is the .NET default. Errors are logged and retried on the next iteration.
- **`MessageRouter` fires push notifications for routed children** — when a `Kind=Message` job fans out into N `Kind=Job` children, the router now captures the pending `JobEnqueued` notifications and fires them after `SaveChanges`. The dispatcher (when `UseDatabasePush()` is enabled) wakes immediately instead of waiting for the next poll. Idle-to-burst pickup latency for messages with many handlers drops from ~`PollingInterval` to &lt;50ms.
- **`DeleteJob` / `RequeueJob` no longer race the worker keep-alive** — `IWarpSqlQueries` previously had two lock-by-id variants: `LockJobByIdAsync` (with `READPAST` / `SKIP LOCKED`) for user commands and `LockJobByIdWaitAsync` (blocking) for orchestration. The `READPAST` variant raced the worker's brief keep-alive UPDATEs and would falsely return "job not found" while the row was being touched. The two methods are now unified onto the blocking `LockJobByIdWaitAsync`; the wait is bounded by the keep-alive's own commit, so it's microseconds in practice.
- **Per-host `DispatcherRegistry` replaces process-wide static** — `WarpDispatcher`'s notification-wakeup signal list was previously a `static List<>` on the class. With multiple `IHost`s in one process (typical in integration tests, possible in some embedded scenarios) the static cross-signaled dispatchers across host boundaries and held references to disposed dispatchers' semaphores. Now a DI singleton scoped to each host's container, registered automatically by `AddWarpWorker`.

### Test Suite Improvements

- **Per-class `IClassFixture` topology** — integration tests no longer share a single `WarpTestServer` per "shard" fixture; each test class gets its own database and each test boots its own server inside the test body. Eliminates whole categories of cross-test interference (leftover `Server` rows poisoning `ServerCleanup`, prior-test mid-flight jobs racing the next test's assertions, SQL Server connection-pool poisoning across server-replacement scenarios). The `[GenerateDatabaseTests(...)]` source generator now emits `[Xunit.IClassFixture<>]` per concrete subclass instead of `[Collection(...)]`.
- **Service Broker is opt-in** — SQL Server Service Broker is no longer always-on for every `_SqlServer` test. Tests that exercise DB push opt in via `[GenerateDatabaseTests(WithPush = true)]`, which routes them to the dedicated `SqlServerPushClassFixture`. Saves significant setup time for the polling-only suite.
- **Diagnostic dump on integration-test failure** — `FixtureDiagnostics.DumpAsync` runs on every failed integration test and prints stuck-job state, recent `ServerTask` / `ServerLog` rows, and a process-wide `ServerLifecycleTrace` of `IHost.StartAsync` / `StopAsync` events. The lifecycle trace is critical because `WarpServerRegistration.StopAsync` deletes its own `Server` row on graceful shutdown, so post-mortem queries against that row return nothing — the in-memory trace is the only source of truth for "did this server actually finish booting / shut down cleanly?" ([#147](https://github.com/moberghr/warp/pull/147), [#151](https://github.com/moberghr/warp/pull/151)).
- **`WarpTestServer.RunHeartbeatOnceAsync`** — test helper that resolves `Heartbeat<TestContext>` in a fresh scope and calls `ExecuteAsync` directly, sidestepping the `ServerTaskHost` auto-loop. Lets tests that disable `HealthCheckInterval` flip `PauseStateHolder` deterministically. Used by the rewritten `PauseServer_JobsStayEnqueued` / `PauseWorkerGroup_JobsStayEnqueued` tests.
- **Full suite stability** — 1,025 tests, ran 5 consecutive full-suite passes with zero flakes (1m 03s – 1m 11s) before tagging this release.

### Website & Docs

- **Enterprise landing page redesign** — new home page with a Moberg-aligned enterprise aesthetic, replacing the prior tagline-driven layout ([#148](https://github.com/moberghr/warp/pull/148), [#150](https://github.com/moberghr/warp/pull/150)).

---

## 0.10.0

*2026-04-27*

The library has been **renamed from Jobly to Warp**. NuGet package IDs change from `Moberg.Jobly.*` to `Moberg.Warp.*`, public types/namespaces from `Jobly.*` to `Warp.*`, default schema from `"jobly"` to `"warp"`, and the dashboard URL from `/jobly` to `/warp`. The old `Moberg.Jobly.*` packages will be deprecated on nuget.org with pointers to the new IDs.

### New Features

- **Auto handler registration** — Handlers and pipeline behaviors register themselves. The `Warp.SourceGenerator` now emits a `[ModuleInitializer]` per consumer assembly that pushes its handler / pipeline-behavior DI registrations into a process-level `WarpGeneratedHandlerRegistry` at assembly load. `AddWarp` replays the registry onto the user's `IServiceCollection` — so nothing else is needed.

  ```csharp
  // before
  services.AddHandlers(typeof(Program).Assembly);
  services.AddPipelineBehaviors(typeof(Program).Assembly);
  services.AddWarp<AppDbContext>(opt => opt.UsePostgreSql());

  // after
  services.AddWarp<AppDbContext>(opt => opt.UsePostgreSql());
  ```

  Works across solution boundaries — each assembly with handlers only needs the `Warp.SourceGenerator` analyzer reference (transitively present via `Moberg.Warp.Core`).

### Improvements

- **Generator covers all behavior kinds** — `IPipelineBehavior<,>` and `IStreamPipelineBehavior<,>` now flow through the source generator alongside `IPublishPipelineBehavior<>` (previously only publish behaviors did, the rest relied on the reflection scan). Open-generic implementations are emitted through the `services.AddTransient(typeof(IFace<>), typeof(Impl<>))` overload, matching the semantics the reflection path provided.
- **`IMessageHandler<T>` multi-handler fix** — The generator's per-message-type handler map was a `Dictionary` that silently overwrote earlier handlers when a message had multiple subscribers. Now it collects them all so pub/sub with N handlers registers N `AddTransient` entries and produces N child jobs — the behavior the reflection path already had, now available to the source-generated path.
- **Scoped behavior scan** — The generator's behavior scan is restricted to the *current* compilation (was walking referenced assemblies via `GetAllTypes`). Core's opt-in addon behaviors (`MutexPipelineBehavior<,>`, `RetryPipelineBehavior<,>`, `CircuitBreakerPipelineBehavior<,>`, `NoRestartPublishBehavior<>`) are still registered only by the explicit `AddMutex` / `AddRetry` / `AddCircuitBreaker` / `AddNoRestart` calls. Core's own compilation short-circuits generation entirely.

### Bug Fixes

- **SQL Server push setup stall is now cancellable** — `SqlServerNotificationTransport.PublishAsync` and `ListenAsync` now await the cached `_setup.Value` via `Task.WaitAsync(ct)` instead of a raw `await`. A stalled broker setup (e.g., schema lock contention on a busy SQL Server) no longer blocks every caller indefinitely — each caller's `CancellationToken` bails out of the wait without invalidating the cached setup task. The next caller with a live token re-awaits and proceeds. This closes the class of intermittent `"Test execution timed out after 30000 milliseconds"` failures in `SqlServerDatabasePushIntegrationTests` under heavy CI contention.
- **Race in `ServerTaskLoop.Signal` fixed** — `Signal()` had a check-then-act TOCTOU on its `SemaphoreSlim(0, 1)`: two threads could both pass `CurrentCount == 0` and both call `Release()`, second throwing `SemaphoreFullException`. Surfaced under dispatcher-mode contention (many workers calling `SignalJobFinalized` concurrently as batches flush) as `"Adding the specified count to the semaphore would cause it to exceed its maximum count"` and cascading downstream failures in the affected task loop. Fixed with a lock around the check-and-release — same pattern `WarpDispatcher.SignalAll` already used. Regression test runs 32 threads × 500 calls concurrently and asserts no exception.

### Test Suite Improvements

- **`TimedFact` default 30s → 10s** — Individual tests should finish in seconds; the 30s default was a band-aid for overly generous inner waits and could hide real hangs. Tests exercising deliberately slow behaviour (retry chains, multi-job integration workloads, two-server orchestration) opt in explicitly with `[TimedFact(N_000)]`. Twenty-three inner-wait timeouts (retry / cancellation / batch / continuation tests) were tightened from 15–30s to 5–10s to match actual runtime, with comfortable headroom for CI jitter.
- **Durability tests for "no job left unprocessed"** — Added three integration tests covering the core recovery guarantees that had no prior coverage:
  - `DispatcherShutdownIntegrationTests.GivenWorkInProgress_WhenServerReplaced_ThenAllJobsEventuallyComplete` — pod-rolling-restart scenario. Server A is disposed mid-flight, server B takes over the same queue; every enqueued job must reach `Completed` via whichever recovery path fires (`UnclaimUndelivered`, channel drain, or `StaleJobRecovery`).
  - `PushFailurePollingBackstopTests.GivenPushEnabledButTransportBroken_WhenJobEnqueued_ThenPollingStillPicksItUp` — proves that when the notification transport is completely broken (both `PublishAsync` and `ListenAsync` throw), polling still delivers the job within a small multiple of `PollingInterval`. Protects the "polling is the correctness backstop for push" invariant.
  - `ListenerReconnectDrainTests.GivenListenerAlwaysFails_WhenJobEnqueued_ThenReconnectDrainStillDelivers` — proves that `NotificationListenerTask.DrainSignals` fires on every reconnect iteration, waking the dispatcher even while the listener connection is permanently down. Jobs enqueued during the listener's offline window are not stranded.
- **`WorkerHostMode` lifecycle smoke tests deflaked** — two `*_CompletesLifecycleWithoutThrowing` tests were occasionally hitting the `TimedFact` budget on SQL Server CI under shared-container contention. Pre-cancel the token passed to `StartAsync` / `StopAsync` so each `BackgroundService.ExecuteAsync` short-circuits on its first `stoppingToken` check — DI wiring + constructors are still exercised, just without the polling loop. Local SQL Server: 360ms → 16ms.
- **SQL Server integration test deflakes** — two test-setup artifacts that surfaced as ~25–50% flake rates on shared SQL Server CI runners. (1) Per-test-server SqlConnection pool isolation: each `WarpTestServer` now gets a unique `Application Name` (which Microsoft.Data.SqlClient includes in the pool key) so a disposed server's cancel-poisoned connections never reach the replacement server's pool — this mirrors production pod-restart, where new process = new pool. Skipped on Npgsql to avoid blowing past `max_connections=100`. (2) The auto `CounterAggregator` 5s sweep was racing test assertions that read `Counter` rows directly; `CounterAggregationInterval` is now `null` in test defaults — every test that needs aggregation already triggers it explicitly via `TestTasks.CreateCounterAggregator`.
- **Full suite runtime** — now **~1m 30s** for 1,024 tests (was ~2m 40s for 947 in 0.8.0), down primarily from the inner-wait tightenings and the shorter `TimedFact` default.

### Migration

Breaking release because of both the rename and the removal of reflection-based registration helpers.

- **Switch to `Moberg.Warp.*` packages** — `Moberg.Jobly.Core` → `Moberg.Warp.Core`, `Moberg.Jobly.UI` → `Moberg.Warp.UI`, `Moberg.Jobly.Worker` → `Moberg.Warp.Worker`, `Moberg.Jobly.Provider.PostgreSql` → `Moberg.Warp.Provider.PostgreSql`, `Moberg.Jobly.Provider.SqlServer` → `Moberg.Warp.Provider.SqlServer`. The old IDs are deprecated on nuget.org but remain restorable.
- **Update namespaces, types, and method calls** — `Jobly.*` → `Warp.*` across all `using` directives and types. `AddJobly` → `AddWarp`, `IJoblyLockProvider` → `IWarpLockProvider`, `IJoblyCredentialValidator` → `IWarpCredentialValidator`, etc. A solution-wide find/replace of `Jobly` → `Warp` (case-sensitive) plus `jobly` → `warp` (lowercase, for env vars / strings) covers it.
- **Database schema** — default schema is now `"warp"` instead of `"jobly"`. Existing deployments must either rename the schema (`ALTER SCHEMA jobly RENAME TO warp;` on PostgreSQL; equivalent on SQL Server) or set `options.Schema = "jobly"` explicitly to keep the old name.
- **Dashboard URL** — `/jobly` → `/warp`. Update any reverse proxy / ingress rules and bookmarked links.
- **Drop the reflection calls** — if your `Program.cs` or test setup calls any of these, delete them:
  ```csharp
  services.AddHandlers(assembly);
  services.AddJobHandlers(assembly);
  services.AddMediatorHandlers(assembly);
  services.AddPipelineBehaviors(assembly);
  ```
  `AddWarp` replaces all of them for consumers that go through the normal DI path.
- **Test harnesses that build `ServiceCollection` manually** — if you construct an `IServiceCollection` directly (e.g. a unit test that doesn't go through `AddWarp`), call `services.AddWarpMediator()` from the generated `Warp.Core.Handlers.Generated` namespace. It stays public as an escape hatch and is idempotent with `AddWarp`.
- **Shared-handler assemblies need the analyzer** — if handlers live in a library that doesn't already reference `Moberg.Warp.Core`, add the source generator as an analyzer so its `[ModuleInitializer]` fires on load:
  ```xml
  <ProjectReference Include="path/to/Warp.SourceGenerator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  ```
  In practice most consumers already pick this up transitively from `Moberg.Warp.Core`.

---

## 0.9.1

*2026-04-21*

### Bug Fixes

- **Docs site build** — `website/docs/features/db-push.md` was referenced from the 0.9.0 release notes but never added, breaking the Docusaurus production build with `onBrokenLinks: 'throw'`. The page is now present and mirrors the DB Push section of the README.

### Maintenance

- **Dashboard UI dependencies** — `npm audit fix` in `src/ui` patches `vite`, `axios`, `hono`, `@hono/node-server`, and `follow-redirects` (5 advisories, 1 high + 4 moderate). No API-surface changes; dashboard bundle hashes change because Vite re-bundles with updated transitive deps.
- **Docs site dependencies** — Docusaurus 3.9.2 → 3.10.0, added `@docusaurus/faster` (required by 3.10 with `future.v4`), and pinned `serialize-javascript ^7.0.5` via `overrides` to close [GHSA-qj8w-gfj5-8c6v](https://github.com/advisories/GHSA-qj8w-gfj5-8c6v) — the build-chain DoS that Docusaurus's bundler transitively pulled in. `npm audit` now reports 0 vulnerabilities on both frontends.

---

## 0.9.0

*2026-04-21*

### New Features

- **Database Push** — Opt-in push notifications replace polling wake-up for the dispatcher, `MessageRoutingTask`, and `OrchestrationTask`. Uses PostgreSQL `LISTEN`/`NOTIFY` or SQL Server Service Broker natively. Enable via `opt.UseDatabasePush()` inside the `AddWarp` / `AddWarpWorker` lambda. Dispatcher pickup drops from ~500ms to &lt;50ms; burst-and-idle workloads roughly halve wall-clock time. Idle deployments see ~16% fewer SELECTs (empty `FOR UPDATE SKIP LOCKED` fetches during idle go away). Worker-fetch push only wires when `UseDispatcher = true` — individual-worker mode keeps polling to avoid thundering-herd. Zero overhead if you don't opt in. See [DB Push](/docs/features/db-push).
- **`State.Scheduled`** — Future-dated jobs (`Schedule(job, at)`) now land in `State.Scheduled` instead of `State.Enqueued` with a future `ScheduleTime`. `ScheduledJobActivationTask` flips them to `Enqueued` when due and fires a `JobEnqueued` push. Cleans up the worker fetch predicate to a pure `CurrentState = Enqueued` check. Pre-upgrade rows still execute correctly thanks to a defensive `ScheduleTime <= now` filter.
- **Per-database Provider Packages** — Provider-specific code moves out of `Warp.Core` / `Warp.Worker` into two new NuGet packages: `Moberg.Warp.Provider.PostgreSql` and `Moberg.Warp.Provider.SqlServer`. Install the one that matches your database and opt in via `opt.UsePostgreSql()` / `opt.UseSqlServer()` inside the registration lambda. Core now stays fully provider-agnostic — no `Npgsql` or `Microsoft.Data.SqlClient` references.
- **Builder-based DI API** — `AddWarp<TContext>(opt => ...)` / `AddWarpWorker<TContext>(opt => ...)` take a single lambda over `IWarpBuilder<TContext>`. Config fields live on the builder directly (inherits `WarpConfiguration`); addons chain as extension methods (`opt.AddRetry()`, `opt.AddMutex()`, `opt.AddCircuitBreaker()`, `opt.AddNoRestart()`, `opt.UseDatabasePush()`).

### Improvements

- **Server-task architecture refactor (`IServerTask`)** — Every background task (`Heartbeat`, `ServerCleanup`, `StaleJobRecovery`, `CounterAggregator`, `ExpirationCleanup`, `RecurringJobScheduler`, `ScheduledJobActivation`, `Orchestrator`, `MessageRouter`) is now a plain DI-registered `IServerTask` service driven by a single `ServerTaskHost<TContext>`. Previously each task was a `BackgroundService` subclass of `ServerTaskBase` — the split separates domain logic from hosting mechanics. Task inner methods that were `public static` for test access are now `internal` instance methods, closing a class of lock-bypass bugs where tests and operators could accidentally invoke work outside the distributed lock.
- **Disable cleanup tasks at the config level** — `CounterAggregationInterval`, `ServerCleanupInterval`, `StaleJobRecoveryInterval`, `ExpirationCleanupInterval`, and `RecurringJobSchedulerInterval` are now `TimeSpan?`. Set any to `null` and the host won't auto-run that task's loop. Useful for multi-tenant setups where you want only one server running cleanup.
- **Signal plumbing consolidated** — `Orchestrator` and `MessageRouter` wake on push events via a single `ServerTaskSignals<TContext>` singleton with named `SignalJobFinalized` / `SignalMessageEnqueued` methods. Tasks declare their subscriptions via `IServerTask.Signals`. Replaces the old static `_instances` lists. No user-visible behaviour change.
- **Heartbeat no longer writes a `ServerLog` row per run** — under the old `ServerTaskBase` default, every server wrote a heartbeat success row every 3s. Now explicit per task via `LogOnSuccess`; heartbeat opts out. Failed heartbeats still log.
- **Faster CI builds** — Test workflow builds only `tests/Warp.Tests/Warp.Tests.csproj` instead of the full solution. Benchmarks, mutation tests, and demo apps no longer built in the test job; they come in transitively only where tests reference them.
- **Atomic-claim fetch** — Worker and dispatcher fetch are now `UPDATE ... RETURNING` (PG) / `UPDATE ... OUTPUT INSERTED.*` (SQL Server) via new `IWarpSqlQueries<TContext>` implementations in the provider packages. Closes the SELECT→UPDATE race window that produced rare double-claims under concurrent-worker load. The old regex-based `RowLockInterceptor` is retired.
- **Shutdown safety** — Three races that could leave jobs as `State=Processing` orphans on shutdown are fixed: `WarpDispatcher` un-claims rows it fetched but didn't deliver; `WarpDispatcherWorker` drains its channel fully before exiting; post-handler bookkeeping uses `CancellationToken.None` so a job whose handler already ran can't be abandoned mid-finalize.
- **Retry / CircuitBreaker respect `State.Scheduled`** — Delayed retries and circuit-breaker reschedules land in `State.Scheduled` when the target time is in the future. New `JobOutcome.RescheduledState(scheduleTime, now)` helper is shared by both pipeline behaviors. No change for immediate retries.
- **Worker host split** — `WarpWorkerSetup` is replaced by three DI-registered `IHostedService`s: `WarpServerRegistration`, `WarpDispatcherHost`, `WarpSingleWorkerHost`. Each mode no-ops when the other is selected via `UseDispatcher`. State flows via a new `ServerRegistrationState` singleton instead of re-querying.
- **Source layout** — Libraries under `src/core/`, providers under `src/core/providers/`, tests in `src/tests/`, demo apps in `src/demo/`. `Directory.Build.props` scoped to match.

### Bug Fixes

- **Queue-name encoding collision (SQL Server)** — `JobHelper` now rejects queue names containing the unit-separator (``) that SQL Server's `STRING_SPLIT` uses internally for encoding. Previously a job published to a ``-containing queue could be delivered to the wrong worker group.

### Migration

Breaking release because of the provider package split and the DI lambda API.

- **Install a provider NuGet**: add `Moberg.Warp.Provider.PostgreSql` or `Moberg.Warp.Provider.SqlServer` alongside `Moberg.Warp.Core`. The provider package registers the row-lock / atomic-claim queries, the exception classifier, and the notification-transport factory — all of which used to live in Core.
- **Wrap registration in a lambda + call the provider**:
  ```csharp
  // before
  services.AddWarp<MyContext>();
  services.AddWarpDatabasePush<MyContext>();   // if using push

  // after
  services.AddWarp<MyContext>(opt =>
  {
      opt.UsePostgreSql();                      // or UseSqlServer()
      opt.UseDatabasePush();                    // if using push, now chains on the builder
  });
  ```
- **`AddWarpDatabasePush<TContext>()` removed** — call `opt.UseDatabasePush()` on the builder instead. Must be after `UsePostgreSql` / `UseSqlServer`.
- **`State.Scheduled` is new** — Future-dated jobs existing from a previous version still execute correctly (worker fetch has a defensive `ScheduleTime <= now` predicate) but won't show in the dashboard's Scheduled list until their time arrives.
- **Retry / CircuitBreaker reschedules land in `State.Scheduled` when delayed** — dashboard filters and any external tooling that queried `Enqueued + future ScheduleTime` should now also look at `Scheduled`.

---

## 0.8.0

*2026-04-19*

### New Features

- **Exponential Polling Backoff** — Workers and the batch-fetch dispatcher now back off geometrically when queues are idle, reducing database load during quiet periods. Configure via `MaxPollingInterval` (default `30s`, ceiling) and `PollingIntervalFactor` (default `2.0`, multiplier). `PollingInterval` becomes the floor. On any processed job, the delay resets to the floor instantly, so throughput under load is unchanged. Paused workers stay at the floor (no compounding while paused). Available on both top-level `WarpWorkerConfiguration` and per-group `WorkerGroupConfiguration`. Set `PollingIntervalFactor = 1.0` to disable backoff. See [Operations → Configuration → Exponential Polling Backoff](/docs/operations/configuration#exponential-polling-backoff).
- **Retry Jitter** — New `JitterFactor` option on `RetryOptions` applies multiplicative random jitter to each computed retry delay: `delay * (1 + JitterFactor * rand(-1, 1))`. Clamped to `[0, 1]`. Global only — no per-job override. Defaults to `0.0` (no jitter) so existing behavior is unchanged. Use to spread retry attempts and avoid thundering herds when many jobs fail at once (e.g. downstream outage). The actual jittered `ScheduleTime` is recorded in the `Requeued` JobLog entry so operators can diagnose from the dashboard.
- **NoRestart (stale-recovery opt-out)** — New addon `AddWarpNoRestart()` lets specific job types stay `Failed` on worker crash instead of being auto-requeued. Apply with `[NoRestart]` / `[Restart]` attributes on the job class (inherits through the class hierarchy), `.WithRestart(bool)` per-enqueue, or flip the global default with `RestartStaleJobsByDefault = false`. Override chain: per-publish > attribute > global. For non-idempotent work (payments, emails, webhooks). See [NoRestart](/docs/features/no-restart).
- **Circuit Breaker** — New addon `AddWarpCircuitBreaker<TContext>()` stops hammering a failing downstream when failures cross a threshold. Opens after `Threshold` consecutive failures, stays open for `Duration`, then transitions to a **half-open state with an atomic probe gate** — exactly one worker probes while others reschedule, preventing thundering herd on recovery. Customise per-handler via `[CircuitBreaker(Group = "...", Threshold = N)]`. Adds a `CircuitBreakerState` entity to your DbContext (migration required). See [Circuit Breaker](/docs/features/circuit-breaker).
- **Batched Completions (Dispatcher Mode)** — When `UseDispatcher = true`, each worker now buffers job completions in memory and commits them as a single multi-row transaction, collapsing N per-job commits into one. Tune via `CompletionBatchSize` (default `50`) and `CompletionFlushInterval` (default `100ms`); set `CompletionBatchSize = 1` to opt out. Poison-entry isolation: a single bad row in a batch of 50 is dropped via recursive split; the other 49 still commit. SIGTERM mid-flush is safe — `FlushAsync` commits to completion using `CancellationToken.None` internally. Trade-off is at-least-once semantics; pair with `[NoRestart]` for non-idempotent handlers. See [Batched Completions](/docs/features/batched-completions).
- **Configurable Log Flush Interval** — New `LogFlushInterval` on `WarpWorkerConfiguration` (default `1s`) controls how often the job monitor drains handler `ILogger` output into the JobLog table. Lower values surface dashboard logs faster at the cost of more DB writes.

### Improvements

- **Resilient worker/dispatcher exception handling** — `WarpWorker` now catches transient exceptions in its poll loop (matching the dispatcher), so a single DB hiccup or handler pipeline fault no longer silently terminates the BackgroundService. The exception path uses a fixed floor delay instead of compounding the polling backoff, so jobs resume within `PollingInterval` of recovery instead of sitting at `MaxPollingInterval` for 30s.
- **`[NoRestart]` and `[Restart]` attributes are inherited** — Declare the policy on a base class (e.g. `PaymentJobBase`) and every derived concrete job inherits it. A derived class with its own attribute overrides the base — closest direct declaration wins.
- **Circuit breaker exception handling narrowed** — The `DbUpdateException` catch in `CircuitBreakerStore.RecordFailureAsync` now only suppresses unique-constraint violations (Npgsql `23505`, SqlClient `2627`/`2601`). CHECK, FK, and column-length violations propagate instead of being silently swallowed.
- **Test suite 48% faster, flake-free** — Disabled idle-polling backoff inside the test server, tuned `StaleJobRecoveryInterval` for tests only, made `LogFlushInterval` configurable, and bumped the `TimedFact` default from 10s to 30s. Full suite (947 tests) runs in ~2m 39s (was ~5m 05s). Eliminated the latent `TimedFact`/`WaitForJobState` timeout-mismatch class of flakes.

### Bug Fixes

- **Dispatcher SIGTERM completion-loss** — Pre-fix, `CompletionBatch.FlushAsync` drained its buffer before observing cancellation, and a shutdown mid-flush silently dropped the drained batch. Now commits with `CancellationToken.None` internally.
- **Circuit breaker thundering herd on half-open** — Without the new HalfOpen CAS, every worker polling when `OpenUntil` lapsed fired a concurrent probe against the recovering downstream. Fix guarantees exactly one probe fires per recovery window.
- **Retry jitter `ScheduleTime` not logged** — The `Requeued` JobLog entry now includes `(next attempt scheduled: <ISO timestamp>)` so operators can see the actual delay jitter applied.
- **MultiServer cross-test flakiness** — A too-aggressive `StaleJobRecoveryInterval` in the test fixture raced worker keep-alive refreshes under two-server SQL Server load, producing sporadic `DbUpdateConcurrencyException`.

### Migration

- **Circuit Breaker migration** — If you register `AddWarpCircuitBreaker<TContext>()`, a new `CircuitBreakerState` entity is added to your DbContext. Run an EF Core migration to create the table (`warp.circuit_breaker_state` under default schema + snake_case naming).
- **`CompletionBatch.FlushAsync` parameter removal** — If you were calling `CompletionBatch.FlushAsync(CancellationToken)` directly (rare — it's internal to `Warp.Worker`), the parameter has been removed. The method now always commits to completion.
- **`[NoRestart]` / `[Restart]` now inherit** — Behaviour change: a base class decorated with `[NoRestart]` now affects every derived concrete job. If you relied on the pre-release `Inherited = false` behaviour, add `[Restart]` on the specific derived types you want to opt back in.
- **`StaleJobRecoveryTask.RequeueStaleJobs` removed** — Replaced by `RecoverStaleJobs` returning `StaleJobRecoveryResult` (includes `Requeued`, `Failed`, `Deleted` counts). No `[Obsolete]` bridge; callers must migrate to the new signature.

---

## 0.7.0

*2026-04-17*

### New Features

- **Stream Requests** — New `IStreamRequest<TResponse>` pattern for lazy, item-by-item streaming via `IAsyncEnumerable<TResponse>`. Extends `IRequest<IAsyncEnumerable<TResponse>>` to preserve the unified type hierarchy — `IPipelineBehavior` applies automatically at the request level. New `IStreamPipelineBehavior<TRequest, TResponse>` wraps the actual enumeration for per-item concerns (timing, transforms). Resolved via `IMediator.CreateStream()`. Source generator provides zero-allocation dispatch.
- **Addon Architecture** — New `Outcome` on `IJobContext` (formerly `FailureOutcome`) lets pipeline behaviors control what happens on both success and failure. The worker is a generic state machine that applies the pipeline's decision. Combined with typed metadata and publish pipeline behaviors, this enables building composable addons (retry, mutex, dead letter queue, circuit breaker) entirely on top of Warp's public API. See [Building Addons](https://github.com/moberghr/warp/blob/main/docs/guides/building-addons.md).
- **Retry Addon** — Retry logic extracted from the worker into an opt-in module at `Warp.Core.Retry`. Declare retry policy with `[Retry(3)]` on either the handler or the job class, override per-enqueue with `new JobParameters().WithRetry(maxRetries: 5)`, or set global defaults via `services.AddWarpRetry(o => { o.MaxRetries = 3; o.Delays = [15, 60, 300]; })`. Priority: per-enqueue metadata > handler attribute > job attribute > global options.
- **Mutex Addon** — Mutex extracted from the worker hot path into an opt-in module at `Warp.Core.Mutex`. Register via `services.AddWarpMutex()`. Set keys with `new JobParameters().WithMutex("payment:123")` or `[Mutex("payment-processing")]` on the job class. Uses the new `IWarpLockProvider` abstraction for distributed locking.
- **Typed Metadata** — Access job metadata through strongly-typed interfaces. Define an interface extending `IJobMetadata`, and read it in handlers via `ctx.GetMetadata<IMyMetadata>()` or configure it at publish time with `new JobParameters().Configure<IMyMetadata>(m => m.CustomerName = "John")`. The source generator produces dictionary-backed implementations. `MetadataSerializer` uses native JSON deserialization for round-trip fidelity (integers stay as `long`, arrays as `List<object>`).
- **Recurring Job Enable/Disable** — Disable a recurring job to temporarily stop it from creating new jobs. The scheduler still fires on schedule but records a "Skipped" entry in the execution history. Re-enabling resumes from the next natural cron occurrence with no catchup burst. API: `POST /api/recurring/{id}/enable|disable`. Dashboard shows Enabled/Disabled badges and Skipped entries in history.
- **Worker Scope Isolation** — Worker and handler now use separate DI scopes. The handler's DbContext lives in its own scope — on failure, the scope is disposed and tracked entities are discarded. No partial handler work leaks into the worker's save. On success, handler changes are committed first (outbox pattern), then Warp state.
- **Extensible Dashboard UI** — New `IWarpUIExtension` interface lets external NuGet packages extend the dashboard without forking. Extensions ship an ES-module as an embedded resource, served at `/warp/_ext/{name}/`. The SPA dynamically imports each module and calls `install(warp)`. Extensions target `data-warp-slot` elements with `mount` / `append` / `insertBefore` / `insertAfter` operations, or register whole new pages via `addPage()`. React, ReactDOM, Axios, and shadcn components are exposed on `window.Warp` so extensions don't bundle them. The built-in `RetryUIExtension` is the reference implementation — renders a retry progress card with attempts/max and next-delay info on the job detail page.

### Improvements

- **Handler Registration Split** — `AddHandlers(assembly)` replaces the old `AddJobHandlers`. New granular methods: `AddJobHandlers` (job + message handlers only), `AddMediatorHandlers` (request + stream handlers only). `AddHandlers` calls both.
- **Dispatcher Split** — `JobDispatcher` (worker job execution) and `MediatorDispatcher` (in-memory request/stream dispatch) are now separate classes with independent method caches.
- **xUnit v3 + Microsoft Testing Platform** — Test suite migrated to xUnit v3 with `UseMicrosoftTestingPlatformRunner`. New `[TimedFact]` / `[TimedTheory]` attributes enforce a 10-second default timeout per test, surfacing deadlocks and hangs globally.
- **Server Memory Benchmarks** — New benchmark project at `src/benchmarks/Warp.ServerBenchmarks/` with four benchmarks (`ScopeMemoryBenchmark`, `WorkerMemoryBenchmark`, `ServerMemoryBenchmark`, `MemoryStressTest`) and a custom `TotalAllocatedDiagnoser` that tracks allocations across all threads. Baseline: ~50 KB per job regardless of scale; 100K-job stress test shows 0.3 MB retained growth (no leak) at 420–496 jobs/sec steady throughput. Documented in [Operations → Benchmarks](/docs/operations/benchmarks).
- **Mutation Testing** — New `Warp.Tests.Mutation` project with an in-memory SQLite fixture runs 293 tests in ~10 seconds, enabling a full Core mutation run in ~30 minutes via `dotnet-stryker`. Baseline scores: **Core 99.60%** (743 killed / 3 survived), Worker 51.53%. Fixed a `RecurringJobPublisher` race condition surfaced during mutation analysis — `AddOrUpdateRecurringJob` now uses `IWarpLockProvider` to prevent duplicate inserts on concurrent calls.
- **.slnx Solution Format** — Migrated from `src/Warp.sln` to `src/Warp.slnx` (XML-based solution format).

### Bug Fixes

- **Recurring job race on concurrent update** — `RecurringJobPublisher.AddOrUpdateRecurringJob` could insert duplicate rows when called concurrently with the same name. Now uses `IWarpLockProvider` for exclusive access during the upsert.
- **Trace page group node highlighting** — Fixed group node highlighting and edge behavior in the trace visualization page.

### Migration

This is a large release with several breaking changes. Plan the upgrade accordingly.

- **Retry is opt-in** — Add `services.AddWarpRetry()` to enable retries. Without it, failed jobs go directly to `Failed`. Replace the removed `maxRetries` publisher overloads and `WarpConfiguration.RetryCount` with `[Retry(n)]` attributes, `new JobParameters().WithRetry(n)`, or the global options callback.
- **Mutex is opt-in** — Add `services.AddWarpMutex()` to enable mutex enforcement. The `JobParameters.Mutex` property is removed — use `.WithMutex("key")` or `[Mutex("key")]` instead. The `ConcurrencyKey` column on the `Job` entity is removed (keys now live in metadata).
- **Typed metadata API** — `IJobContext<T>` / `JobContext<T>` are removed. Read typed metadata via `ctx.GetMetadata<IMyMetadata>()` and configure at publish via `new JobParameters().Configure<IMyMetadata>(m => ...)`.
- **Reduced public surface** — `JobHelper`, `JobDispatcher`, `MetadataSerializer`, and EF interceptors are now `internal`. The Retry and Mutex addons demonstrate that everything needed to build addons is available through the public API — no `InternalsVisibleTo` needed.
- **Database migration required** — New `DisabledAt` column on `RecurringJob`, `Skipped` column on `RecurringJobLog`, `ConcurrencyKey` dropped from `Job`. Run an EF Core migration after upgrading.
- **Solution file renamed** — Update build scripts and IDE shortcuts from `Warp.sln` to `Warp.slnx`.

### Stats

- ~770 tests (PostgreSQL + SQL Server) + 293 SQLite mutation tests
- Core mutation score: 99.60% (746 mutants, 743 killed)

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
- **Span Attributes** — Job execution spans include OTel semantic convention tags (`messaging.system`, `messaging.destination.name`, `messaging.operation.name`, `messaging.message.id`) and Warp-specific tags (`warp.job.type`, `warp.job.kind`, `warp.job.status`, `warp.job.duration_ms`, `warp.job.retry_count`). Failed spans are marked with `ActivityStatusCode.Error`.
- **Span Events** — Key lifecycle moments recorded as events on the span: `warp.job.completed`, `warp.job.failed` (with exception info), `warp.job.retried` (with retry/max counts), `warp.job.cancelled`.
- **OTel Metrics** — Four `System.Diagnostics.Metrics` instruments via a `Meter` named `"Warp"`: `warp.job.duration` (histogram, ms), `warp.job.active` (up-down counter), `warp.job.completed` (counter with status tag), `warp.job.enqueued` (counter with kind tag). All tagged by queue and type for filtering.
- **Automatic Log Correlation** — `AddWarpWorker` configures `ActivityTrackingOptions` so TraceId, SpanId, and ParentId appear in log output by default. No additional configuration needed.

### Integration

All features are on by default with zero configuration. To export to OTel backends:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Warp"))
    .WithMetrics(m => m.AddMeter("Warp"));
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
- **Naming Convention Support** — Entity configurations respect EF Core naming conventions (e.g., `UseSnakeCaseNamingConvention()`). All Warp tables default to the `warp` schema, configurable via `WarpConfiguration.Schema`.
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

- [GitHub Release](https://github.com/moberghr/warp/releases/tag/0.4.0)

---

## 0.3.0

*2026-04-07*

### New Features

- Initial public release with core job processing, message queue, in-memory mediator, dashboard, recurring jobs, batches, cancellation, mutex, crash recovery, and tracing.

### Links

- [GitHub Release](https://github.com/moberghr/warp/releases/tag/0.3.0)
