# Spec: WarpBackgroundService — dashboard-visible BackgroundService analog

## Problem

.NET's `BackgroundService` is the canonical primitive for long-lived in-process work — Kafka consumers, periodic syncs that don't fit the polling cadence of a `Job`, connection-holding workers, internal crons. Warp users currently have no managed option:

- `IJob` is request-shaped and finite; it doesn't model "this thing runs for the life of the process."
- `IServerTask` is Warp's internal supervised-loop primitive (Heartbeat, MessageRouter, Orchestrator, ScheduledJobActivation, etc.). It's tick-based — host calls `ExecuteAsync` per interval — which is the wrong shape for "I own my loop." It's also explicitly Warp-internal (§2.3) and was not designed to be a public extension point.
- Raw `BackgroundService` works but is invisible to operators: no dashboard surface, no cluster-singleton coordination, no automatic restart with backoff, no migration story for the captive-scoped-dependency foot-gun.

Result: users either drop to raw `BackgroundService` (losing observability) or wrap the work as a recurring `IJob` with a tight interval (wasting a worker slot and forcing single-pass shape on long-lived work). Neither is right.

The shape users actually want is *"a BackgroundService, but Warp manages it and it shows up in the dashboard."* That's this feature.

## Solution (v1)

### Public API

Abstract base class in a new namespace `Warp.Core.BackgroundServices`. One-line migration from `BackgroundService` is a hard requirement.

```csharp
public abstract class WarpBackgroundService
{
    public virtual string Name => GetType().Name;

    public virtual ServiceScope Scope => ServiceScope.PerServer;

    public virtual LogLevel MinLogLevel => LogLevel.Information;

    public virtual int? LogRetentionCountOverride => null;

    public virtual TimeSpan? LogRetentionAgeOverride => null;

    protected abstract Task ExecuteAsync(CancellationToken ct);
}

public enum ServiceScope
{
    PerServer = 1,
    Singleton = 2,
}
```

Per `§8.11`, enum values start at 1.

User code:

```csharp
public sealed class KafkaDrainService : WarpBackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<KafkaDrainService> _logger;

    public KafkaDrainService(IServiceScopeFactory scopes, ILogger<KafkaDrainService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public override ServiceScope Scope => ServiceScope.Singleton;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // ... drain a batch, commit offsets, etc.
            _logger.LogInformation("Drained {Count} messages", count);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}

// Registration
services.AddWarpWorker<AppDbContext>(opt =>
{
    opt.UsePostgreSql();
    opt.AddBackgroundService<KafkaDrainService>();
});
```

`AddBackgroundService<T>()` is a builder extension on `WarpWorkerConfiguration`. Registers `T` as singleton lifetime; aliases the registration so the host can discover via `GetServices<WarpBackgroundService>()`; idempotent on double-registration. `IBackgroundServiceQueryService` and storage services are added by the same call (the addon-discovery marker is presence of `IBackgroundServiceQueryService` in DI — same pattern as Concurrency / RateLimits / Sagas at `WarpAddonsInfo`).

**Lifetime contract.** `WarpBackgroundService` subclasses are singletons. Users inject `IServiceScopeFactory` and own their per-work-unit scope creation — identical to the recommended pattern for plain `BackgroundService`. `ValidateScopes = true` (already on in dev/test per `feedback_dbcontext_options_scoped`) catches captive-scoped-dependency bugs at startup.

### Persistence

Four new entities, all in `warp` schema (configurable per §5.6), naming via existing snake_case convention.

| Entity | Key | Lifecycle | Purpose |
|---|---|---|---|
| `BackgroundServiceDefinition` | `Name` (PK) | Created on first registration; persists forever (audit) | One row per service Name across the cluster. Owns `DeclaredScope` (mismatch anchor), `FirstSeenAt`, `LastSeenAt`. Forward-compat for future `IsPaused`, per-service retention overrides. |
| `BackgroundServiceInstance` | `(ServerId, ServiceName)` (composite PK) | Insert on host start; delete on graceful shutdown; `ServerCleanup` deletes on ungraceful | One row per (server, service). Owns `DeclaredScope` (the mismatch check compares this against `Definition.DeclaredScope`), `Status`, `StartedAt`, `LastHeartbeatAt`, `LastError`, `LastErrorAt`, `RestartCount`. |
| `BackgroundServiceLease` | `ServiceName` (PK) | Created/updated on acquisition; deleted on graceful release or `ServerCleanup` | Singleton coordination only. Owns `HolderServerId`, `LeaseExpiresAt`. No `Fence` column in v1 — retrofittable in one migration if non-idempotent external-write users surface. |
| `BackgroundServiceLog` | `Id` (identity, PK) | Inserted by collector flush; deleted by retention sweep + cascade from `Instance` | Captured user log entries + lifecycle events. Owns `(InstanceId FK cascade, Timestamp, Level, Source enum, Message 4KB, ExceptionType?, ExceptionMessage 4KB?)`. |

Status enum (§8.11 — start at 1):

```csharp
public enum BackgroundServiceStatus
{
    Running = 1,
    Waiting = 2,
    Faulted = 3,
    Restarting = 4,
    ConfigurationMismatch = 5,
}
```

Source enum (§8.11):

```csharp
public enum BackgroundServiceLogSource
{
    Lifecycle = 1,
    User = 2,
}
```

Entities live in `Warp.Core.Data.Entities` (§8.13 — data entity namespace convention). EF Core configurations in `Warp.Core/Data/Configurations/BackgroundService*Configuration.cs`, registered via `WarpModelCustomizer`. **No FK cascades except `Log → Instance`** — every other table follows the existing Warp convention of explicit deletion paths (§5.4 implicit, called out at `WarpServerRegistration.cs:125` for `WorkerGroup`). `Log → Instance` cascade is the one exception because logs are dependent rows that have no meaning without their instance.

### Singleton coordination via lease (not Medallion long-held locks)

A `DistributedLock.Postgres` / `DistributedLock.SqlServer` advisory lock held for the lifetime of `ExecuteAsync` (hours-to-days) is fragile: connection-scoped locks die silently if a network middlebox drops the idle TCP connection, releasing the lock without notifying the running user code — instant split-brain. Lease-based coordination is the standard distributed-systems answer (Kubernetes Lease, Zookeeper sessions, etcd leader election).

**Lease semantics:**

- **TTL:** 30 seconds.
- **Renewal:** piggybacked on the existing `Heartbeat` server task (~3s cadence via `WarpWorkerConfiguration.HealthCheckInterval`). Extends `IWarpSqlQueries.HeartbeatAsync` to also issue:
  ```sql
  UPDATE background_service_instance
    SET last_heartbeat_at = @now
    WHERE server_id = @me;

  UPDATE background_service_lease
    SET lease_expires_at = @now + interval '30 seconds'
    WHERE holder_server_id = @me AND lease_expires_at > @now;
  ```
  The lease-renewal UPDATE returns the affected service names. Any singleton this server holds whose row was *not* renewed (e.g., row deleted, holder cleared, expired) = lease lost.
- **Loss detection → CTS cancellation.** Lost-lease names are published to a new `ServerTaskSignals` channel `BackgroundServiceLeaseLost`. The supervisor for that singleton subscribes; on signal, it cancels the per-service CTS. User code observes cancellation via the CT passed to `ExecuteAsync`.
- **Acquisition (waiting server, ~15s poll):**
  ```sql
  UPDATE background_service_lease
    SET holder_server_id = @me, lease_expires_at = @now + interval '30 seconds'
    WHERE service_name = @n AND (holder_server_id IS NULL OR lease_expires_at < @now)
    RETURNING service_name;
  ```
  Zero rows → still held by someone, retry on next poll. One row → acquired, transition `Waiting → Running`, enter `ExecuteAsync`.
- **Worst-case failover:** ~30s on hard-kill (lease must expire). **~0s on graceful shutdown** — `WarpServerRegistration.StopAsync` issues `DELETE FROM background_service_lease WHERE holder_server_id = @me` *before* waiting on user code so a hung `ExecuteAsync` cannot strand the lease.

**HealthCheckInterval = null** (test config, §4.6): supervisor's own renewal pass on the supervisor loop ensures the lease stays fresh; tests drive `WarpTestServer.RunHeartbeatOnceAsync` explicitly per existing pattern.

### Configuration mismatch — refuse to start

`Scope` is declared on the class, so all binaries with the same DLL agree. The failure mode is rolling deploys where `Scope` changed between versions:

- v1 declares `Scope = PerServer`: every server runs an independent copy.
- v2 declares `Scope = Singleton`: would-be holders acquire the lease, would-be waiters sit in `Waiting`.

During the deploy, v1 servers don't even touch the lease — they just run. The cluster has *N+1* running instances (1 lease holder + N v1 per-server copies). Silent split-brain caused by configuration drift.

**Mitigation: refuse to start on mismatch.** On host start, each server compares its declared scope against `Definition.DeclaredScope`. Any disagreement → insert `Instance` row with `Status = ConfigurationMismatch` and skip the supervisor loop entirely. The dashboard surfaces this loudly. Operator resolves by completing or rolling back the deploy.

Race-safe via the unique constraint on `Definition.Name` — first server to register inserts the Definition with its scope; subsequent registrations compare-and-bail.

### Lifecycle — always-on, no opt-out

`ExecuteAsync` that throws OR returns without a graceful-cancellation signal is treated as fault. **Always restart.** No `MaxAttempts`, no `[NoRestart]`, no stop button in v1.

**Restart backoff:** exponential 1s → 2s → 4s → 8s → 16s → 30s (cap). `Status` transitions: `Running → Faulted → Restarting → Running`.

**Healthy-reset:** if `ExecuteAsync` ran for ≥5 minutes uninterrupted before this fault, reset `RestartCount = 0` and backoff to 1s. Captures "transient blip on a healthy service" vs "fast-crash loop."

**Singleton-specific:** on fault, release the lease *before* the backoff wait so a healthier server can take over. The faulted server re-enters lease acquisition after backoff like any other waiter.

**Graceful return** (user's `ExecuteAsync` returns without the CT being cancelled): treated as fault. Lifecycle log row: `Faulted (graceful exit)`. Restart. Rationale: a `BackgroundService` that returns early without cancellation is almost always a bug, and silent stop ("service stopped working at 2am, nobody noticed for a week") is the worst-possible failure mode.

### Graceful shutdown (30s default, global config)

`WarpServerRegistration.StopAsync` already runs a 10s-budgeted fresh-CTS cleanup pass for `Server` / `Worker` / `WorkerGroup` rows (`WarpServerRegistration.cs:100`). The new `BackgroundServiceHost.StopAsync` runs in parallel with:

1. **Signal cancellation** to every running service's CTS.
2. **Best-effort `DELETE` of `Lease` and `Instance` rows for @me.** *Issued immediately, not awaited.* This is the failover-speed lever; a hung user `ExecuteAsync` must not strand the lease.
3. **Wait up to `ShutdownTimeout` (global config, default 30s)** for `ExecuteAsync` to return.
4. **Move on.** If user code ignored the CT, it's abandoned at process exit — same semantics as plain `BackgroundService.StopAsync` with timeout.

No `StopAsync` override hook in v1. Users do flush logic in `ExecuteAsync`'s `catch (OperationCanceledException)` / `finally`.

### Ungraceful cleanup — extend `ServerCleanup`

Existing `ServerCleanup.CleanUpServersAsync` (`ServerCleanup.cs:49`) removes `Worker` + `WorkerGroup` rows for timed-out `Server`s. Extend the same loop to also remove `BackgroundServiceInstance` + `BackgroundServiceLease` rows for the dead server. Same explicit-deletion pattern, no FK cascade.

### Log capture — out-of-the-box, with guardrails

**Auto-wired `ILoggerProvider`.** When the host starts an instance, it adds a `BackgroundServiceLoggerProvider` to the per-supervisor scope, filtered to the service's category (`MyApp.Services.KafkaDrainService` etc.). User code's existing `ILogger<MyService>.LogInformation(...)` calls flow to **both** the user's normal log stack (Serilog/console/whatever — unchanged) **and** Warp's `BackgroundServiceLogCollector`.

**Collector.** Per-instance singleton. Buffers entries, flushes every ~1s like `JobLogCollector` (§8.15). One batched INSERT per flush.

**Guardrails (all enforced by the collector):**

1. **Level filter.** Default `MinLogLevel = Information`. Below the threshold = dropped. Configurable per-service via `WarpBackgroundService.MinLogLevel` override.
2. **Rate cap.** Sustained >100 captured entries/sec → 10s drop-window. On entering drop-mode, emit one synthetic `Warning` row: `"log capture rate-limited; dropping entries"`. On exiting: one synthetic `Information` row: `"log capture resumed; dropped N entries during rate limit"`.
3. **Message truncation.** `Message` and `ExceptionMessage` capped at 4096 bytes; longer values truncated and suffixed with `…[truncated]`.
4. **Retention.** Per instance:
   - Count cap: 1000 rows (configurable global default `WarpConfiguration.BackgroundServiceLogRetentionCount`, per-service override `LogRetentionCountOverride`).
   - Age cap: 7 days (configurable global default `WarpConfiguration.BackgroundServiceLogRetentionAge`, per-service override `LogRetentionAgeOverride`).
   Enforced by extending `ExpirationCleanup` (existing retention task per §8.9) — same pattern used for `RecurringJobLog`.

**Lifecycle events** (`Started`, `LeaseAcquired`, `LeaseLost`, `Faulted`, `Restarting`, `Stopped`, `ConfigurationMismatch`) emitted by `BackgroundServiceLifecycleLogger` to the same `BackgroundServiceLog` table with `Source = Lifecycle`. Dashboard filters by source.

**§1.2 PII responsibility** documented (XML docs on the base class + website docs); not enforced. Users who log payload contents are responsible — same posture as `JobLog.Message` per existing §1.2. No `[NoCapture]` attribute in v1.

### Dashboard

**Addon discovery.** Extend `WarpAddonsInfo` (`src/core/Warp.UI/Endpoints/WarpAddonsInfo.cs`) with `public bool Services { get; init; }`. The existing `/api/addons` endpoint handler populates it via `serviceProvider.GetService<IBackgroundServiceQueryService>() != null` — same DI-presence pattern as `Concurrency`, `RateLimits`, `Sagas`. Frontend reads the existing `/api/addons` round-trip at boot (per §2.10) and gates the new `/warp/services` nav off `addons.services`. **No new probe endpoint** (the old hide-on-404 pattern was replaced cluster-wide by the addon-discovery commit `9701531`).

**REST routes** (auto-generated via `[WarpHttpGet]` per §8.10):

- `GET /api/services` — list aggregated per service Name. Returns `Definition` + per-instance summary + lease info.
- `GET /api/services/{name}` — detail with all `Instance` rows for that service.
- `GET /api/services/{name}/logs?source=&level=&fromId=&limit=` — paginated log tail.
- `GET /api/services/{name}/lease` — lease detail (singleton only; 404 if `PerServer`).

**Pages** (in `src/ui/src/pages/BackgroundServices/`):

- `List.tsx` — one row per service Name. Columns: Name, Scope, status summary ("Running 3/3" or "Running on server-X, 2 waiting"), aggregate `RestartCount`, last error type if any. Configuration-mismatch indicator. Polling ~2s while open.
- `Detail.tsx` — header with name, scope, retention settings. Per-instance tabs (one tab per server). Each tab: status, started-at, last-heartbeat, restart-count, full captured exception (not truncated on display — capture is already 4KB-capped). Lease panel (singleton only): holder, expires-at countdown. Log tail filterable by `Source` and `Level`. Polling ~2s while open.

REST polling only in v1. No `DashboardPush` integration (deferred).

**Auth** inherits `WarpAuthorizationFilter` (§1.5).

### Telemetry

`WarpTelemetry` counters following the pattern of `warp.sagas.*` (§8.17):

- `warp.background_services.started` (counter, tag `service_name`).
- `warp.background_services.faulted` (counter, tags `service_name`, `exception_type`).
- `warp.background_services.lease_lost` (counter, tag `service_name`).
- `warp.background_services.restart_count` (gauge or observable, per service).

**No per-execution activity spans** — they'd be hours-long, which breaks every trace viewer. The `Started` / `Faulted` counters are enough for "is the cluster healthy" alerting.

### Host

`BackgroundServiceHost<TContext> : BackgroundService` in `Warp.Worker.BackgroundServices` namespace, alongside `ServerTaskHost`. Discovers all `WarpBackgroundService` instances from DI on `StartAsync`, spins one `BackgroundServiceSupervisor` per registered service. **Worker hot path untouched (§0.2 / §6.1 preserved).**

### Code organization — SOLID / SRP

Each class owns one responsibility.

**Public (`Warp.Core.BackgroundServices`):**

- `WarpBackgroundService` — abstract base, data + abstract method only. No behavior, no DI.
- `ServiceScope`, `BackgroundServiceStatus`, `BackgroundServiceLogSource` — enums.
- `WarpBackgroundServiceBuilderExtensions` — `AddBackgroundService<T>()` extension on `WarpWorkerConfiguration`.

**Entities (`Warp.Core.Data.Entities`, per §8.13):**

- `BackgroundServiceDefinition`, `BackgroundServiceInstance`, `BackgroundServiceLease`, `BackgroundServiceLog` — data-only POCOs.

**EF Configurations (`Warp.Core.Data.Configurations`):**

- `BackgroundServiceDefinitionConfiguration`, `BackgroundServiceInstanceConfiguration`, `BackgroundServiceLeaseConfiguration`, `BackgroundServiceLogConfiguration` — one per entity, registered via `WarpModelCustomizer`.

**Services (`Warp.Core.BackgroundServices`):**

- `IBackgroundServiceQueryService` + `BackgroundServiceQueryService<TContext>` — read-only dashboard queries. Returns DTOs only. **Presence in DI is the addon-discovery marker.**
- `IBackgroundServiceStateService` + `BackgroundServiceStateService<TContext>` — `Instance` row CRUD. One method per state transition.
- `IBackgroundServiceLeaseCoordinator` + `BackgroundServiceLeaseCoordinator<TContext>` — `Lease` table primitives: `TryAcquireAsync`, `ReleaseAsync`. (Renewal is done in the heartbeat batch query; coordinator doesn't own it.)

**Host (`Warp.Worker.BackgroundServices`):**

- `BackgroundServiceHost<TContext>` — discovery + supervisor lifecycle. Mirrors `ServerTaskHost`.
- `BackgroundServiceSupervisor<TContext>` — per-service supervisor loop. Owns restart loop, exponential backoff, healthy-reset timer, fault logging. Polymorphic over `IBackgroundServiceStrategy`.
- `IBackgroundServiceStrategy` — strategy interface. One method: `Task<ExecutionScope?> AcquireAsync(CancellationToken ct)`. Returns `null` (skip — waiting for lease) or an `ExecutionScope` carrying the CT + a release `IAsyncDisposable`. Open/closed for future scope kinds.
- `PerServerServiceStrategy` — always returns a non-null `ExecutionScope` immediately.
- `SingletonServiceStrategy` — wraps `IBackgroundServiceLeaseCoordinator.TryAcquireAsync`. Subscribes to `BackgroundServiceLeaseLost` signal.
- `BackgroundServiceLogCollector` — singleton per service-instance. Buffer + flush + rate cap + truncation. Owns nothing else.
- `BackgroundServiceLoggerProvider` + `BackgroundServiceLogger` — `ILoggerProvider` / `ILogger` plumbing. Pure pipeline, no policy.
- `BackgroundServiceLifecycleLogger` — named methods (`LogStarted`, `LogLeaseAcquired`, `LogFaulted`, etc.) so the supervisor doesn't sprinkle magic strings.

**HTTP (`Warp.UI` + `Warp.Http`):**

- `GetBackgroundServices`, `GetBackgroundService`, `GetBackgroundServiceLogs`, `GetBackgroundServiceLease` — one `[WarpHttpGet]` handler per endpoint. Thin: delegate to `IBackgroundServiceQueryService`.
- `WarpAddonsInfo.Services` + one-line check in the existing addons handler.

**Frontend (`src/ui/`):**

- `pages/BackgroundServices/List.tsx`, `Detail.tsx`.
- `api/backgroundServices.ts` — typed client.
- One nav entry in `MainLayout.tsx` gated by `addons.services`.

**Cleanup integration (no new files):**

- `WarpServerRegistration.StopAsync` — two extra `ExecuteDeleteAsync` calls.
- `ServerCleanup.CleanUpServersAsync` — same loop pattern as existing `Worker` / `WorkerGroup` cleanup.
- `ExpirationCleanup` — extends with `BackgroundServiceLog` retention sweep (count cap + age cap).

## Test strategy

Every test uses `[GenerateDatabaseTests]` for PG + SQL Server coverage where DB state matters; NoDb otherwise. No `Task.Delay` for synchronization (§4.5). No `[TimedFact(N_000)]` budget raises to fix flakes (§4.4). `BarrierSignal` for any concurrency assertion (§4.7). TimeProvider fake + `WarpTestServer.RunHeartbeatOnceAsync` for any time-based behavior.

**NoDb tests (~12 cases):**

- `AddonsEndpointTests` extension — two new cases: `Services=true` when `IBackgroundServiceQueryService` registered; `Services=false` when absent. Wire-shape lock for `"services":` in camelCase.
- `WarpBackgroundServiceBuilderTests` — `AddBackgroundService<T>()` registers `T` as singleton; resolves as `WarpBackgroundService`; idempotent on double-call; throws on non-`WarpBackgroundService` type (compile-time generic constraint preferred).
- `BackgroundServiceLogCollectorTests` — buffer / flush; level filter drops below `MinLogLevel`; rate cap engages at >100/sec and emits synthetic Warning; truncation at 4KB.
- `CaptiveScopedDependencyTests` — `ValidateScopes = true` causes `BuildServiceProvider` to throw when a `WarpBackgroundService` subclass takes a scoped DbContext directly.
- `BackgroundServiceTelemetryTests` — counters fire on the expected events via `TestMeterListener`.

**DB integration tests (~14 cases × 2 backends):**

- `ConfigurationMismatchTests` — seed `Definition.DeclaredScope = PerServer`, start host with service declared `Singleton`; assert `Instance.Status = ConfigurationMismatch` and supervisor never enters user code (sentinel counter stays at 0). Symmetric case.
- `PerServerLifecycleTests` (`FixtureKind.Integration`) — start one server with a test service that increments a counter and awaits a `BarrierSignal`; assert `Instance.Status = Running`, `StartedAt` set, barrier reached. Cancel host; assert row deleted on graceful path.
- `SingletonAcquisitionTests` (`FixtureKind.MultiServer`) — two servers, same singleton service. Pin user code at a `BarrierSignal`. Assert exactly one reaches the barrier; assert holder's `Lease.HolderServerId` + `Instance.Status = Running`; the other has `Status = Waiting`.
- `SingletonFailoverTests` (`FixtureKind.MultiServer`) — cancel the holder; advance `TimeProvider` past lease TTL; run `RunHeartbeatOnceAsync` on the waiter; assert waiter transitions to `Running`.
- `LeaseRenewalTests` — advance `TimeProvider` by 10s, run heartbeat, assert `LeaseExpiresAt` extended. Advance past TTL without renewal, assert acquire predicate frees the lease.
- `LeaseLossCtsCancellationTests` (`FixtureKind.MultiServer`) — holder runs; manually expire its lease in DB; run heartbeat for holder; assert renewal UPDATE returns 0 rows for that service; assert CTS cancelled; assert user code's `catch (OperationCanceledException)` ran (sentinel).
- `RestartBackoffTests` — test service throws on first call, succeeds on second; assert `Status` walks `Running → Faulted → Restarting → Running`; `RestartCount = 1`; `BackgroundServiceLog` has a `Faulted` lifecycle row with the exception type. `TimeProvider`-driven backoff advance.
- `HealthyResetTests` — service runs >5min (via `TimeProvider` advance) then throws; assert `RestartCount` reset to 0 on the next fault.
- `GracefulReturnTreatedAsFaultTests` — service returns immediately without CT cancellation; assert `Faulted` lifecycle log + restart.
- `GracefulShutdownOrderingTests` — pin user code at `BarrierSignal`; cancel host; assert lease DELETE issued and lease row gone within ~100ms (well before the 30s shutdown wait would elapse); unblock barrier; assert clean exit.
- `UngracefulCleanupTests` — manually insert `Instance` + `Lease` row attached to a `Server` with stale heartbeat; run `ServerCleanup` once; assert both rows deleted.
- `LogCaptureTests` (`FixtureKind.Integration`) — test service logs `Information` and `Warning` via `ILogger<TestService>`; assert `BackgroundServiceLog` rows with `Source = User`; `Debug` log → no row (below default threshold); set `MinLogLevel = Debug` on the service → `Debug` row appears.
- `LogRetentionTests` — insert 1500 log rows on an instance; run `ExpirationCleanup`; assert 500 oldest deleted. Insert old rows >7 days; assert age-based cleanup.
- `DashboardQueryShapeTests` — seed `Definition` + `Instance` + `Lease` rows; assert `IBackgroundServiceQueryService.ListAsync()` returns aggregated view; `GetAsync(name)` returns per-instance detail; `GetLeaseAsync(name)` populated for singletons, null for per-server.

Test handlers live in `src/tests/Warp.Tests/TestData/BackgroundServices/`. Each is empty / sentinel-increment / barrier-await (§4.10).

## Out of scope (v1)

- **Pause.** Column not added to `Definition` (YAGNI per the design discussion). The table is forward-compatible: `IsPaused bool DEFAULT false` is a single migration when needed. Pause semantics (per the design discussion): service-wide pause via `Definition.IsPaused = true`; singleton holder releases lease, all servers transition to `Paused`; per-server services cancel CT and transition.
- **Fence tokens** for non-idempotent external work. Column dropped from `Lease`. Retrofittable as one migration (add `Fence` column + `protected long? Fence` property on base class) when a user surfaces the use case.
- **DashboardPush event broadcasting** for service state changes. REST polling only in v1. Extension point exists via `ServerTaskSignals` for v1.x.
- **Force-restart action** in dashboard. Always-on lifecycle; users redeploy if they need a hard reset.
- **Per-service `ShutdownTimeout` override.** One global knob in v1.
- **`StopAsync` override hook** on the base class. Users use `catch (OperationCanceledException)` / `finally` blocks inside `ExecuteAsync`.
- **Source-generator validation** of `WarpBackgroundService` subclasses (e.g., flagging `ExecuteAsync` that returns synchronously). Revisit if user mistakes show up.
- **Per-instance pause** (pause `MyService` on `server-B` specifically). Out of scope even when pause lands — limited operator value, complicates singleton semantics.
- **`[NoCapture]` attribute** for marking log scopes/calls as "don't write to DB." Documented PII responsibility instead. Revisit if real-world traffic surfaces a need.

## Risks

- **Heartbeat batch-query expansion.** Adding two UPDATEs to `IWarpSqlQueries.HeartbeatAsync` is a hot-path change (every server, every 3s). Mitigation: both UPDATEs are scoped by `server_id = @me`, hit indexed columns, and join the existing CTE / table-variable structure — measured cost should be ≤10% added to the existing heartbeat latency. Benchmark via `src/benchmarks/` after Batch 6.
- **Captive-scoped-dep foot-gun is silent in production.** `ValidateScopes = true` catches it in Development per `feedback_dbcontext_options_scoped`, but a user who runs Release without that flag set will hit a slow leak. Mitigation: document loudly in XML docs and website docs; existing memory note is the canonical reference.
- **§1.2 PII leakage via auto-captured logs.** Users who log payload contents to `ILogger<MyService>` will see that data land in the `BackgroundServiceLog` table, which is more broadly visible than their normal log stack. Mitigation: documented + `MinLogLevel` override.
- **Heartbeat dependency for lease renewal.** If `HealthCheckInterval` is misconfigured to a value approaching the 30s TTL, leases will flap. Mitigation: documented invariant — `HealthCheckInterval` should be ≤ TTL / 5; tests assert default config satisfies this.
- **Frontend nav gate races boot.** If `/api/addons` is slow on first paint, the `/warp/services` nav blinks in. Mitigation: existing one-shot retry on transient failure in `MainLayout.tsx` (per the addon-discovery commit) covers this; no new work.
