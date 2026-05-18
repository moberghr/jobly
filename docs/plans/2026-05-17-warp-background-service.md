# Plan: WarpBackgroundService — dashboard-visible BackgroundService analog

Spec: `docs/specs/2026-05-17-warp-background-service.md`. JSON sidecar: `docs/specs/2026-05-17-warp-background-service.json`.

**9 batches. Subagent path applies** (manifest > 6 files, `security_impact = low` because of dashboard auth surface + log capture).

Pure addition. No renames, no removals, no compatibility shims. Batches build bottom-up: entities (Batch 1) → public API + DI (Batch 2) → state + lease services (Batch 3) → log infrastructure (Batch 4) → host + supervisor + strategies (Batch 5) → heartbeat + cleanup integration (Batch 6) → dashboard backend (Batch 7) → dashboard frontend (Batch 8) → telemetry + docs (Batch 9). Each batch leaves the build green and the existing test suite green.

## Batch 1 — Entities + EF configurations + model customizer

**Goal:** Put the four tables on disk on both backends. No behavior yet — this batch is "I can CRUD the rows directly via DbContext."

**Files (new):**

- `src/core/Warp.Core/Data/Entities/BackgroundServiceDefinition.cs` — POCO: `Name (string, PK)`, `DeclaredScope (ServiceScope)`, `FirstSeenAt (DateTime)`, `LastSeenAt (DateTime)`.
- `src/core/Warp.Core/Data/Entities/BackgroundServiceInstance.cs` — POCO: `ServerId (Guid)`, `ServiceName (string)`, `DeclaredScope (ServiceScope)`, `Status (BackgroundServiceStatus)`, `StartedAt (DateTime)`, `LastHeartbeatAt (DateTime)`, `LastError (string?, max 4096)`, `LastErrorAt (DateTime?)`, `RestartCount (int)`. Composite PK `(ServerId, ServiceName)`.
- `src/core/Warp.Core/Data/Entities/BackgroundServiceLease.cs` — POCO: `ServiceName (string, PK)`, `HolderServerId (Guid)`, `LeaseExpiresAt (DateTime)`.
- `src/core/Warp.Core/Data/Entities/BackgroundServiceLog.cs` — POCO: `Id (long, identity, PK)`, `ServerId (Guid)`, `ServiceName (string)`, `Timestamp (DateTime)`, `Level (LogLevel)`, `Source (BackgroundServiceLogSource)`, `Message (string, max 4096)`, `ExceptionType (string?, max 512)`, `ExceptionMessage (string?, max 4096)`. FK target is the `(ServerId, ServiceName)` composite on `Instance` — use shadow nav or explicit FK config.
- `src/core/Warp.Core/Data/Configurations/BackgroundServiceDefinitionConfiguration.cs` — `IEntityTypeConfiguration<BackgroundServiceDefinition>`. `Metadata.SetSchema(schema)`. Max length 256 on `Name`.
- `src/core/Warp.Core/Data/Configurations/BackgroundServiceInstanceConfiguration.cs` — composite PK; FK to `Definition.Name` with `OnDelete(DeleteBehavior.Restrict)`; max lengths; index on `(ServerId)` for `ServerCleanup` predicate.
- `src/core/Warp.Core/Data/Configurations/BackgroundServiceLeaseConfiguration.cs` — PK `ServiceName`; FK to `Definition.Name` with `OnDelete(DeleteBehavior.Restrict)`; index on `(HolderServerId)` for `ServerCleanup` predicate.
- `src/core/Warp.Core/Data/Configurations/BackgroundServiceLogConfiguration.cs` — identity PK; FK composite `(ServerId, ServiceName)` → `Instance` with `OnDelete(DeleteBehavior.Cascade)` — **this is the only cascade in the feature**; index on `(ServerId, ServiceName, Id DESC)` for the dashboard log-tail query.

**Files (new — public enums, placed here so Batch 2 can use them but the entity layer can compile alone):**

- `src/core/Warp.Core/BackgroundServices/ServiceScope.cs` — `enum { PerServer = 1, Singleton = 2 }`.
- `src/core/Warp.Core/BackgroundServices/BackgroundServiceStatus.cs` — `enum { Running = 1, Waiting = 2, Faulted = 3, Restarting = 4, ConfigurationMismatch = 5 }`.
- `src/core/Warp.Core/BackgroundServices/BackgroundServiceLogSource.cs` — `enum { Lifecycle = 1, User = 2 }`.

**Files (modify):**

- `src/core/Warp.Core/Data/WarpModelCustomizer.cs` — register the four configurations.
- `src/tests/Warp.Tests/TestData/TestContext.cs` — DbSet exposure (or rely on `Set<T>()` via the customizer).

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/EntitySchemaTests.cs` (`[GenerateDatabaseTests(FixtureKind.Default)]`):**

- `Insert_DefinitionThenInstance_Succeeds` — full happy-path round-trip on both backends.
- `Insert_InstanceWithoutDefinition_ThrowsRestrict` — FK restrict enforced (Definition must exist).
- `Insert_LeaseWithoutDefinition_ThrowsRestrict` — same.
- `Delete_Instance_CascadesLogRows` — insert instance + 5 logs; delete instance; assert logs gone.
- `Delete_Definition_ThrowsWhenInstancesExist` — restrict semantics.
- `Schema_UsesSnakeCase` — model-inspection assert; columns are `last_heartbeat_at` etc.

**Checkpoint:** `dotnet build src/Warp.slnx` clean. New tests pass on both backends.

**Risk:** EF Core's snake-case naming convention applied via `UseSnakeCaseNamingConvention()` may mangle FK column names on the composite. Mitigation: assert explicit FK column names in `BackgroundServiceLogConfiguration` if the default mapping fails.

## Batch 2 — Public API (base class + builder extension)

**Goal:** User-facing surface. Build red against the host (which doesn't exist yet); the only thing executable here is `AddBackgroundService<T>()` registering DI shapes.

**Files (new):**

- `src/core/Warp.Core/BackgroundServices/WarpBackgroundService.cs` — abstract base. `virtual string Name => GetType().Name`, `virtual ServiceScope Scope => ServiceScope.PerServer`, `virtual LogLevel MinLogLevel => LogLevel.Information`, `virtual int? LogRetentionCountOverride => null`, `virtual TimeSpan? LogRetentionAgeOverride => null`, `protected abstract Task ExecuteAsync(CancellationToken ct)`. XML docs include the captive-scoped-dep warning and PII-responsibility note.
- `src/core/Warp.Core/BackgroundServices/WarpBackgroundServiceBuilderExtensions.cs` — `AddBackgroundService<T>(this WarpWorkerConfiguration opt) where T : WarpBackgroundService` extension. Generic constraint enforces base class at compile time. Registers `T` as singleton, aliases `WarpBackgroundService` to it. Idempotent — second call for same `T` is a no-op via service-collection scan.

**Files (modify):**

- `src/core/Warp.Core/Configuration.cs` — add three properties on `WarpConfiguration`:
  - `TimeSpan BackgroundServiceShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);`
  - `int BackgroundServiceLogRetentionCount { get; set; } = 1000;`
  - `TimeSpan BackgroundServiceLogRetentionAge { get; set; } = TimeSpan.FromDays(7);`

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/WarpBackgroundServiceBuilderTests.cs` (NoDb):**

- `AddBackgroundService_RegistersTypeAsSingleton`.
- `AddBackgroundService_ResolvesAsWarpBackgroundService` (alias works).
- `AddBackgroundService_CalledTwice_IsIdempotent` — only one resolved instance via `GetServices<WarpBackgroundService>()`.
- `WarpBackgroundService_NameDefault_IsTypeName`.
- `WarpBackgroundService_ScopeDefault_IsPerServer`.
- `WarpBackgroundService_MinLogLevelDefault_IsInformation`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/CaptiveScopedDependencyTests.cs` (NoDb):**

- `BuildServiceProvider_WithScopedDepOnBackgroundService_ThrowsWithValidateScopes`.

**Checkpoint:** Build clean. New tests pass. Existing tests unaffected.

## Batch 3 — Storage services (state + lease)

**Goal:** Instance row CRUD and Lease primitives behind clean interfaces. SRP isolation.

**Files (new):**

- `src/core/Warp.Core/BackgroundServices/IBackgroundServiceStateService.cs` — interface. Methods:
  - `Task<RegistrationOutcome> RegisterAsync(string name, ServiceScope declaredScope, CancellationToken ct)` — upserts Definition (insert-if-missing); inserts/updates Instance for `(@me, name)`; returns `Registered` or `ConfigurationMismatch` based on `Definition.DeclaredScope` comparison.
  - `Task SetStatusAsync(string name, BackgroundServiceStatus status, CancellationToken ct)`.
  - `Task RecordFaultAsync(string name, Exception ex, CancellationToken ct)` — sets `Status=Faulted`, `LastError`, `LastErrorAt`, `RestartCount++`.
  - `Task ResetRestartCountAsync(string name, CancellationToken ct)`.
  - `Task DeleteAsync(string name, CancellationToken ct)`.
- `src/core/Warp.Core/BackgroundServices/BackgroundServiceStateService.cs` — `BackgroundServiceStateService<TContext> : IBackgroundServiceStateService where TContext : DbContext`. Injects `TContext`, `TimeProvider`, `IOptions<WarpWorkerConfiguration>` (for `ServerId`).
- `src/core/Warp.Core/BackgroundServices/IBackgroundServiceLeaseCoordinator.cs` — interface. Methods:
  - `Task<bool> TryAcquireAsync(string serviceName, TimeSpan ttl, CancellationToken ct)` — atomic UPDATE with the spec's predicate.
  - `Task ReleaseAsync(string serviceName, CancellationToken ct)` — `DELETE WHERE HolderServerId = @me`.
- `src/core/Warp.Core/BackgroundServices/BackgroundServiceLeaseCoordinator.cs` — `BackgroundServiceLeaseCoordinator<TContext>`. Uses `ExecuteUpdateAsync` / `ExecuteDeleteAsync` — no tracked entities (high-frequency path).

**Files (modify):**

- `src/core/Warp.Core/BackgroundServices/WarpBackgroundServiceBuilderExtensions.cs` — `AddBackgroundService<T>` also registers `IBackgroundServiceStateService` and `IBackgroundServiceLeaseCoordinator` as scoped if not already present. Idempotent.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/BackgroundServiceStateServiceTests.cs` (`[GenerateDatabaseTests(FixtureKind.Default)]`):**

- `RegisterAsync_FirstCall_InsertsDefinitionAndInstance` — assert both rows.
- `RegisterAsync_DefinitionExistsSameScope_InsertsInstanceOnly`.
- `RegisterAsync_DefinitionExistsDifferentScope_ReturnsConfigurationMismatch` — assert Instance row exists with `Status=ConfigurationMismatch`.
- `SetStatusAsync_UpdatesStatusAndPersistsAcrossContexts`.
- `RecordFaultAsync_SetsLastErrorAndIncrementsRestartCount`.
- `ResetRestartCountAsync_SetsRestartCountToZero`.
- `DeleteAsync_RemovesInstanceRow`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/BackgroundServiceLeaseCoordinatorTests.cs` (`[GenerateDatabaseTests(FixtureKind.Default)]`):**

- `TryAcquireAsync_NoExistingLease_ReturnsTrueAndInsertsRow`.
- `TryAcquireAsync_ExpiredLease_ReturnsTrueAndTakesOver`.
- `TryAcquireAsync_LiveLeaseHeldByOtherServer_ReturnsFalse`.
- `TryAcquireAsync_LiveLeaseHeldByMe_ReturnsTrueAndExtends` (idempotent re-acquire).
- `ReleaseAsync_DeletesOnlyOwnLease` — assert only @me's row removed.

**Checkpoint:** Build clean. New tests pass on both backends. Existing tests green.

## Batch 4 — Log capture infrastructure

**Goal:** Auto-wired `ILoggerProvider` + collector with buffering, level filter, rate cap, truncation. No host integration yet — wires up via DI but isn't invoked yet.

**Files (new):**

- `src/core/Warp.Core/BackgroundServices/BackgroundServiceLogCollector.cs` — per-instance singleton. Internal queue (bounded), flush timer (~1s), rate-cap state machine. Public surface:
  - `void Enqueue(BackgroundServiceLogSource source, LogLevel level, string message, Exception? exception)`.
  - `Task FlushAsync(CancellationToken ct)` — drains queue, single batched INSERT via injected scope factory.
  - `IDisposable Subscribe(...)` for the flush timer; disposed on shutdown.
- `src/core/Warp.Core/BackgroundServices/BackgroundServiceLoggerProvider.cs` — `ILoggerProvider`. Created per-supervisor, knows its `serviceName` and `minLogLevel`. `CreateLogger(category)` returns `BackgroundServiceLogger` only when category matches the service's type's full name.
- `src/core/Warp.Core/BackgroundServices/BackgroundServiceLogger.cs` — `ILogger`. Forwards to the collector with the configured `minLogLevel` gate. Implements `IsEnabled` and `BeginScope` (no-op scope).
- `src/core/Warp.Core/BackgroundServices/BackgroundServiceLifecycleLogger.cs` — wraps the collector with named methods: `LogStarted()`, `LogLeaseAcquired()`, `LogLeaseLost(reason)`, `LogFaulted(Exception)`, `LogRestarting(int attempt, TimeSpan delay)`, `LogStopped()`, `LogConfigurationMismatch(declaredScope, otherScope)`. Each method calls `Enqueue` with `Source = Lifecycle`.

**Files (modify):**

- `src/core/Warp.Worker/Services/ExpirationCleanup.cs` — extend with a `BackgroundServiceLog` retention sweep: delete rows where `Id < (Id of Nth-from-newest for that instance)` (count cap) AND where `Timestamp < now - retentionAge` (age cap). Use the configured global defaults from `WarpConfiguration`, fall back to per-service overrides if reachable via `Instance → Definition` join (defer per-service to v1.x if it complicates the SQL; for v1, global config is enough).

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/BackgroundServiceLogCollectorTests.cs` (NoDb):**

- `Enqueue_BelowMinLogLevel_NotBuffered`.
- `Enqueue_AtOrAboveMinLogLevel_Buffered`.
- `Flush_DrainsBufferToBatchInsert` (via fake scope factory).
- `MessageOver4KB_Truncated_AppendsTruncationMarker`.
- `RateCap_SustainedAbove100PerSec_EntersDropModeAndEmitsWarning`.
- `RateCap_AfterDropWindow_ResumesAndEmitsSummary`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/LogRetentionTests.cs` (`[GenerateDatabaseTests(FixtureKind.Default)]`):**

- `ExpirationCleanup_InstanceWith1500Logs_Keeps1000NewestDeletesRest`.
- `ExpirationCleanup_LogsOlderThan7Days_Deleted`.
- `ExpirationCleanup_LogsCascadeWhenInstanceDeleted` (FK cascade smoke test).

**Checkpoint:** Build clean. New tests pass. Existing `ExpirationCleanup` tests still green.

## Batch 5 — Host, supervisor, strategies

**Goal:** The runtime. Discovers services, spins one supervisor per, runs restart loop. This is the largest batch — break into clearly named files per the SRP plan.

**Files (new):**

- `src/core/Warp.Worker/BackgroundServices/BackgroundServiceExecutionScope.cs` — record/struct: `(CancellationToken Token, IAsyncDisposable? Release)`.
- `src/core/Warp.Worker/BackgroundServices/IBackgroundServiceStrategy.cs` — single method: `Task<BackgroundServiceExecutionScope?> AcquireAsync(CancellationToken ct)`. Returns null when waiting (e.g., singleton with live lease elsewhere).
- `src/core/Warp.Worker/BackgroundServices/PerServerServiceStrategy.cs` — always returns a non-null scope with the supervisor's CT.
- `src/core/Warp.Worker/BackgroundServices/SingletonServiceStrategy.cs` — wraps `IBackgroundServiceLeaseCoordinator.TryAcquireAsync` with `ttl = WarpWorkerConfiguration.BackgroundServiceLeaseTtl ?? TimeSpan.FromSeconds(30)`. Polls every ~15s (configurable). Subscribes to `BackgroundServiceLeaseLost` signal — on signal, cancels the active scope's CT.
- `src/core/Warp.Worker/BackgroundServices/BackgroundServiceSupervisor.cs` — per-service. Receives `WarpBackgroundService` instance, `IBackgroundServiceStrategy`, `IBackgroundServiceStateService`, `BackgroundServiceLifecycleLogger`, `IServiceScopeFactory`, `TimeProvider`, `ILogger`. Owns the restart loop:
  1. Register (state service); if mismatch → log lifecycle event, sit out.
  2. Loop: `strategy.AcquireAsync()` → if null, wait poll interval and retry.
  3. If scope acquired: `SetStatus(Running)`, lifecycle log Started, invoke `service.ExecuteAsync(scope.Token)`.
  4. Catch: lifecycle log Faulted, `RecordFault`, release scope, exponential backoff (1→30s cap), lifecycle log Restarting, healthy-reset check (>5min successful → `RestartCount=0`, backoff=1s).
  5. Loop again until host CT cancels.
- `src/core/Warp.Worker/BackgroundServices/BackgroundServiceHost.cs` — `BackgroundServiceHost<TContext> : BackgroundService where TContext : DbContext`. Constructor mirrors `ServerTaskHost<TContext>` — resolves `IServiceScopeFactory`, `IWarpLockProvider` (unused but available for future strategies), `TimeProvider`, `ILoggerFactory`, `IOptions<WarpWorkerConfiguration>`, `ServerTaskSignals<TContext>`. In ctor (or `StartAsync`): discover all `WarpBackgroundService` instances; build one supervisor per. `ExecuteAsync(stoppingToken)` runs `Task.WhenAll(supervisors)`. `StopAsync` issues fire-and-forget DELETE of lease + instance rows for @me, then awaits up to `ShutdownTimeout`.

**Files (modify):**

- `src/core/Warp.Core/Events/ServerTaskSignal.cs` (or wherever the channel enum lives) — add `BackgroundServiceLeaseLost`.
- `src/core/Warp.Core/Events/ServerTaskSignals.cs` — wire the channel; `Publish(name)` overload so callers can include the lost service name.
- `src/core/Warp.Worker/WarpWorkerConfiguration.cs` (or wherever it lives) — add `TimeSpan? BackgroundServiceLeaseTtl { get; set; }` (nullable, default null → 30s in coordinator) and `TimeSpan? BackgroundServiceAcquirePollInterval { get; set; }` (default null → 15s).
- `src/core/Warp.Core/BackgroundServices/WarpBackgroundServiceBuilderExtensions.cs` — `AddBackgroundService<T>` also registers `BackgroundServiceHost<TContext>` as a hosted service (one-time, idempotent guard).

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/ConfigurationMismatchTests.cs` (`[GenerateDatabaseTests(FixtureKind.Integration)]`):**

- `ScopeMismatch_DefinitionPerServer_HostDeclaresSingleton_StatusConfigurationMismatch`.
- `ScopeMismatch_DefinitionSingleton_HostDeclaresPerServer_StatusConfigurationMismatch`.
- `ScopeMismatch_SupervisorDoesNotInvokeUserCode` — sentinel counter stays at 0.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/PerServerLifecycleTests.cs` (`[GenerateDatabaseTests(FixtureKind.Integration)]`):**

- `Start_PerServerService_InsertsInstanceWithStatusRunning`.
- `Start_PerServerService_ReachesUserCodeBarrier` (BarrierSignal pin).
- `GracefulShutdown_DeletesInstanceRow`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/SingletonAcquisitionTests.cs` (`[GenerateDatabaseTests(FixtureKind.MultiServer)]`):**

- `TwoServers_OneSingletonService_OnlyOneReachesBarrier`.
- `TwoServers_HolderHasStatusRunningAndLeaseRow`.
- `TwoServers_WaiterHasStatusWaiting`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/RestartBackoffTests.cs` (`[GenerateDatabaseTests(FixtureKind.Integration)]`):**

- `ThrowingService_FirstCallFailsSecondSucceeds_StatusWalksFaultedRestartingRunning`.
- `ThrowingService_RestartCountIncrementsOnFault`.
- `ThrowingService_BackoffFollowsExponentialCurve` (via TimeProvider).
- `BackoffCapsAt30Seconds`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/HealthyResetTests.cs` (`[GenerateDatabaseTests(FixtureKind.Integration)]`):**

- `ServiceRanFor5Min_ThenFaults_RestartCountResetsToZero`.
- `ServiceRanFor4Min_ThenFaults_RestartCountIncrements` (boundary).

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/GracefulReturnTreatedAsFaultTests.cs` (`[GenerateDatabaseTests(FixtureKind.Integration)]`):**

- `ServiceReturnsWithoutCancellation_StatusGoesFaulted_LifecycleLogRecordsGracefulExit`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/GracefulShutdownOrderingTests.cs` (`[GenerateDatabaseTests(FixtureKind.Integration)]`):**

- `Shutdown_LeaseDeletedBeforeWaitingOnExecuteAsync` (pin user code, assert lease row gone within deterministic window before barrier release).

**Tests (new) — `src/tests/Warp.Tests/TestData/BackgroundServices/CountingService.cs`, `BarrierPinnedService.cs`, `ThrowingService.cs`, `LoggingService.cs`** — empty / counter / barrier-await / throwing test handlers per §4.10.

**Checkpoint:** Build clean. All Batch 5 tests pass on both backends. Full suite still green.

**Risk:** Discovery in `ServerTaskHost`-style ctor scope can deadlock if a `WarpBackgroundService` ctor takes scoped deps under `ValidateScopes = true`. Mitigation: discovery uses a dedicated scope per the `ServerTaskHost.cs:39` pattern; ctor failures are caught + logged + skipped, not propagated.

## Batch 6 — Heartbeat integration, cleanup integration

**Goal:** Wire lease renewal into the existing `Heartbeat` server task, wire `BackgroundServiceLeaseLost` signal, extend cleanup paths.

**Files (modify):**

- `src/core/Warp.Core/Data/Queries/IWarpSqlQueries.cs` — extend `HeartbeatAsync` signature OR add `RenewBackgroundServicesAsync(server_id, now, ttl, ct)` returning `IReadOnlyList<string>` of lost-lease service names. Two-query approach is cleaner (separate from the heartbeat row update) but adds a round-trip; one-query batch via CTE keeps the existing latency. **Decision: extend `HeartbeatAsync` to also return lost-lease names in the same batch** — matches the §2.3 commitment to one round-trip per heartbeat.
- `src/core/Warp.Core/Data/Queries/HeartbeatResult.cs` — add `IReadOnlyList<string> LostLeases` field.
- `src/core/Warp.Provider.PostgreSql/WarpSqlQueries.cs` — extend the CTE to include `UPDATE background_service_instance SET last_heartbeat_at = ...` and `UPDATE background_service_lease SET lease_expires_at = ... RETURNING ...` for held leases; compute lost names as "leases I held last beat that are no longer mine."
- `src/core/Warp.Provider.SqlServer/WarpSqlQueries.cs` — same with table-variable / OUTPUT INTO pattern.
- `src/core/Warp.Worker/Services/Heartbeat.cs` — receive `LostLeases` from the result; publish each name to `ServerTaskSignals.Publish(BackgroundServiceLeaseLost, name)`.
- `src/core/Warp.Worker/WarpServerRegistration.cs` — extend `StopAsync` with two `ExecuteDeleteAsync` calls for `Instance` and `Lease` rows scoped to `@me`. Run in parallel via `Task.WhenAll` to keep within the 10s budget.
- `src/core/Warp.Worker/Services/ServerCleanup.cs` — extend `CleanUpServersAsync` loop to also remove `BackgroundServiceInstance` + `BackgroundServiceLease` rows for each dead server, following the existing `Worker`/`WorkerGroup` pattern at lines 70–82.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/LeaseRenewalTests.cs` (`[GenerateDatabaseTests(FixtureKind.Default)]`):**

- `Heartbeat_HolderRenewsLease_LeaseExpiresAtAdvances`.
- `Heartbeat_NotHolder_NoLeaseUpdate`.
- `Heartbeat_LeaseLostBetweenBeats_ReturnsServiceNameInLostList`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/LeaseLossCtsCancellationTests.cs` (`[GenerateDatabaseTests(FixtureKind.MultiServer)]`):**

- `LeaseManuallyStolen_HeartbeatPublishesLost_SupervisorCancelsCts_UserCodeObservesCancellation` (BarrierSignal-pinned user code that releases on CT signal).

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/SingletonFailoverTests.cs` (`[GenerateDatabaseTests(FixtureKind.MultiServer)]`):**

- `HolderDies_LeaseExpiresViaTtl_WaiterTakesOver` (TimeProvider advances past TTL, run heartbeat on waiter).
- `HolderGracefulShutdown_LeaseDeletedImmediately_WaiterTakesOverWithoutTtlWait`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/UngracefulCleanupTests.cs` (`[GenerateDatabaseTests(FixtureKind.Default)]`):**

- `ServerCleanup_DeadServerWithBackgroundServiceRows_RowsRemoved`.

**Checkpoint:** Build clean. All Batch 6 tests pass. **Critical**: existing `HeartbeatSqlQueryTests` still pass — the extended query must be backward-compatible when no `BackgroundServiceLease` rows exist.

**Risk:** CTE expansion in `HeartbeatAsync` might increase heartbeat latency past acceptable. Mitigation: micro-bench via `src/benchmarks/` before merging. If above 10% added cost, fall back to two queries (separate `RenewBackgroundServicesAsync`).

## Batch 7 — Dashboard backend (query service + endpoints + addon flag)

**Goal:** Expose data to the dashboard. Wire the addon-discovery flag. No frontend yet.

**Files (new):**

- `src/core/Warp.Core/BackgroundServices/IBackgroundServiceQueryService.cs` — interface. Methods:
  - `Task<IReadOnlyList<BackgroundServiceListItemDto>> ListAsync(CancellationToken ct)`.
  - `Task<BackgroundServiceDetailDto?> GetAsync(string name, CancellationToken ct)`.
  - `Task<BackgroundServiceLeaseDto?> GetLeaseAsync(string name, CancellationToken ct)`.
  - `Task<IReadOnlyList<BackgroundServiceLogDto>> GetLogsAsync(string name, BackgroundServiceLogSource? source, LogLevel? minLevel, long? fromId, int limit, CancellationToken ct)`.
- `src/core/Warp.Core/BackgroundServices/BackgroundServiceQueryService.cs` — `BackgroundServiceQueryService<TContext>`. `.Select()` projections (§5.3, §6.4), `AsNoTracking()`, no `_context.Set<>()` subqueries inside `Select()` (§5.2 / `feedback_no_context_subqueries`).
- `src/core/Warp.UI/Endpoints/BackgroundServices/GetBackgroundServices.cs` — `[WarpHttpGet("/api/services")]`. Delegates to `ListAsync`.
- `src/core/Warp.UI/Endpoints/BackgroundServices/GetBackgroundService.cs` — `[WarpHttpGet("/api/services/{name}")]`. 404 when null.
- `src/core/Warp.UI/Endpoints/BackgroundServices/GetBackgroundServiceLogs.cs` — `[WarpHttpGet("/api/services/{name}/logs")]`. Query string for filters.
- `src/core/Warp.UI/Endpoints/BackgroundServices/GetBackgroundServiceLease.cs` — `[WarpHttpGet("/api/services/{name}/lease")]`. 404 for per-server services.

**Files (modify):**

- `src/core/Warp.UI/Endpoints/WarpAddonsInfo.cs` — add `public bool Services { get; init; }`.
- `src/core/Warp.UI/Endpoints/WarpAddonsEndpoint.cs` (or wherever the `/api/addons` handler lives — likely created in commit 9701531) — add `Services = serviceProvider.GetService<IBackgroundServiceQueryService>() != null` to the response.
- `src/core/Warp.Core/BackgroundServices/WarpBackgroundServiceBuilderExtensions.cs` — `AddBackgroundService<T>` also registers `IBackgroundServiceQueryService` (scoped) — **this is the DI marker for addon discovery**.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/DashboardQueryShapeTests.cs` (`[GenerateDatabaseTests(FixtureKind.Default)]`):**

- `ListAsync_AggregatesPerServiceName`.
- `ListAsync_SurfacesConfigurationMismatch`.
- `GetAsync_UnknownName_ReturnsNull`.
- `GetAsync_KnownName_ReturnsAllInstanceRows`.
- `GetLeaseAsync_PerServerService_ReturnsNull`.
- `GetLeaseAsync_SingletonWithLease_ReturnsLeaseDto`.
- `GetLogsAsync_FilterBySource_Lifecycle_OnlyLifecycleRows`.
- `GetLogsAsync_FilterByMinLevel_DropsBelowThreshold`.
- `GetLogsAsync_PaginateByFromId_ReturnsExpectedSlice`.

**Tests (modify) — `src/tests/Warp.Tests/Admin/AddonsEndpointTests.cs`:**

- Add `Services=true` registered + `Services=false` absent cases.
- Extend the camelCase wire-shape test to include `"services":`.
- Extend the per-addon permutation theory with the new flag.

**Checkpoint:** Build clean. `dotnet test --filter-trait Category=NoDb` covers the addon-endpoint additions. DB-integration tests pass on both backends.

## Batch 8 — Dashboard frontend

**Goal:** Pages + nav gating + API client.

**Files (new):**

- `src/ui/src/api/backgroundServices.ts` — typed Axios calls for the four endpoints. Result types in `types/index.ts`.
- `src/ui/src/pages/BackgroundServices/List.tsx` — table view. ~2s polling.
- `src/ui/src/pages/BackgroundServices/Detail.tsx` — header + per-instance tabs (Radix tabs to match existing UI), lease panel (conditional), log tail with filter dropdowns.

**Files (modify):**

- `src/ui/src/types/index.ts` — add DTO TypeScript types matching the C# DTOs.
- `src/ui/src/layouts/MainLayout.tsx` — add nav entry `"Services" → /warp/services`, gated by `addons.services === true`. Existing addon discovery (`useAddons` hook or similar from commit 9701531) handles the fetch.
- `src/ui/src/App.tsx` (or wherever routes are declared) — register `/warp/services` and `/warp/services/:name` routes.

**Tests:** dashboard frontend doesn't have unit tests in this codebase per the existing pattern (no test runner under `src/ui/`). Manual smoke test as part of Phase 4 review. Dev-server flow per CLAUDE.md tech stack section: `cd src/ui && npm install && npm run dev` (Vite on :5173).

**Checkpoint:** `npm run build` succeeds. Manual smoke per spec: list page shows the test service, detail page renders both tabs, log filter works.

## Batch 9 — Telemetry, CLAUDE.md, website docs

**Goal:** Counters + docs. The "make it discoverable + observable" closer.

**Files (modify):**

- `src/core/Warp.Core/Logging/WarpTelemetry.cs` — add `BackgroundServicesStarted` (counter), `BackgroundServicesFaulted` (counter, tagged `service_name` + `exception_type`), `BackgroundServicesLeaseLost` (counter), `BackgroundServicesRestartCount` (observable gauge or counter). Fire from the supervisor.
- `src/core/Warp.Worker/BackgroundServices/BackgroundServiceSupervisor.cs` — wire counter increments at the appropriate state-transition points.
- `CLAUDE.md` — add §8.18 entry summarizing the feature, following the §8.17 (saga) shape.
- `.claude/rules/architecture.md` — add §2.13 entry parallel to §2.10 (DashboardPush) noting `WarpBackgroundService` is an opt-in addon, signal channel `BackgroundServiceLeaseLost`, the lease-based coordination model.
- `.claude/rules/project-specific.md` — extend §8.x with the addon's pause/fence/restart conventions.
- `website/docs/features/background-services.md` — full feature doc: migration from `BackgroundService`, scope choice, lease semantics, captive-scoped-dep warning, PII responsibility, dashboard tour.
- `README.md` — one-line bullet under the feature list.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/BackgroundServiceTelemetryTests.cs` (NoDb via fake supervisor invocation or NoDb integration if simpler):**

- `Started_CounterFires`.
- `Faulted_CounterFiresWithExceptionTypeTag`.
- `LeaseLost_CounterFires`.
- `RestartCount_GaugeReflectsCurrentValue`.

**Tests (new) — `src/tests/Warp.Tests/BackgroundServices/LogCaptureTests.cs` (`[GenerateDatabaseTests(FixtureKind.Integration)]`):**

- `UserLogAtInformation_CapturedToDb`.
- `UserLogAtDebug_NotCaptured_DefaultThreshold`.
- `UserLogAtDebug_Captured_WhenMinLogLevelDebug`.
- `LifecycleEventsCaptured_WithSourceLifecycle`.

**Checkpoint:** `dotnet build src/Warp.slnx` clean. Full suite passes on both backends. `dotnet format --verbosity quiet` clean. `npm run build` clean.

## Post-implementation review (Phase 4)

- **Stage 1 (compliance)** — invoke `compliance-reviewer` against the full diff + behavioral diff. Checks: §0.x critical rules, §1.2 PII, §5.x data layer, §6.1 worker hot path untouched, §8.13 entity namespace, public-API-only addon composition (no `InternalsVisibleTo`).
- **Stage 2 (parallel)** — `test-reviewer` (public behavior covered, BarrierSignal usage correct, no Task.Delay) + `architecture-reviewer` (SRP holds, strategy boundaries clean, no captive deps, signal pipe used correctly).

## Risks summary

- Heartbeat CTE expansion may push latency. Mitigate by benching after Batch 6.
- Captive-scoped-dep silent in production. Documented + `ValidateScopes` in dev.
- PII in captured logs. Documented + `MinLogLevel` override.
- Snake-case FK naming on composite. Explicit configuration if EF defaults misbehave.
- Lifetime — singleton WarpBackgroundService consuming scoped DI. `ValidateScopes=true` + tests.
