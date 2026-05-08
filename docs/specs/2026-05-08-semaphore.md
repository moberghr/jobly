# Spec: Unified concurrency control — `[Mutex]` + `[Semaphore]` over a shared primitive

## Problem

Warp's `Mutex` addon (PR #159, shipped 2026-05-07) caps job concurrency per key at exactly one — `Skip` mode drops the surplus, `Wait` mode requeues it. It cannot say *"up to N jobs per key Processing at a time"* — the canonical "limit concurrent calls to an external API to 5" pattern.

A mutex is a semaphore with `maxCount = 1`. The two are the same primitive; the distinction is naming convention, not function. `Medallion.Threading` (Warp's underlying lock library) literally implements `IDistributedLock` as `IDistributedSemaphore` with `maxCount = 1`. Hangfire keeps both `[Mutex]` and `[Semaphore]` attributes for the user mental-model split — engineers reach for one or the other depending on intent — and Warp will too.

This work refactors the existing addon into a single shared primitive (`Warp.Core.Concurrency` namespace) under one pipeline behavior, one metadata type, one admin layer. `[Mutex]` and `[Semaphore]` both live in that namespace as user-facing attribute names. `[Mutex]` is *fixed at limit = 1* — the way to express "limit > 1" is `[Semaphore("key", 5)]`. This split keeps the semantics of each word honest: a mutex provides mutual exclusion (1), a semaphore provides N concurrent slots.

The user has explicitly accepted breaking changes for this work — naming choices optimize for clarity, not for keeping import statements stable. Renames from main:

- Namespace: `Warp.Core.Mutex` → `Warp.Core.Concurrency` (folder + namespace)
- Enum: `MutexMode` → `ConcurrencyMode`
- Metadata: `IMutexMetadata` → `IConcurrencyMetadata`
- Pipeline behavior: `MutexPipelineBehavior` → `ConcurrencyPipelineBehavior`
- Publish behavior: `MutexPublishBehavior` → `ConcurrencyPublishBehavior`
- Service config method: `AddMutex()` → `AddConcurrency()`
- Lock-key prefix: `warp:mutex:` → `warp:concurrency:`
- Dashboard path: `/warp/concurrency`

Reference designs studied: Hangfire.Throttling, Sidekiq Enterprise, Faktory Enterprise. See `docs/specs/2026-05-08-semaphore-brainstorm.md` for the survey.

## Solution (v1)

### One shared primitive, two named attributes

Acquisition primitive: new `IWarpSemaphoreProvider.TryAcquireAsync(name, maxCount, timeout, ct)`. `IWarpLockProvider` stays — it has other callers (recurring-job leader election, server-task locking) that don't need a slot count.

**Postgres implementation strategy.** `Medallion.Threading.Postgres` does not expose a counted-semaphore primitive (Postgres advisory locks are exclusive-only — verified against `DistributedLock.Postgres` 1.3.0 API surface and upstream docs). To support N-slot semantics on PG without inventing migration infrastructure (Warp ships entity configurations, not migrations — users run `dotnet ef migrations add` themselves; ref `website/docs/getting-started.md:45`), the provider uses the **N-distinct-named-locks trick** (verified to be the same algorithm Medallion's own `SqlSemaphore` uses on SQL Server — see `src/DistributedLock.SqlServer/SqlSemaphore.cs`): derive lock names `{name}:0`, `{name}:1`, ..., `{name}:{maxCount-1}` and `pg_try_advisory_lock` each in turn, returning the first that succeeds. **Start at a random offset** (mirroring Medallion's design) to spread concurrent acquirers across slots and reduce contention on slot 0. At `maxCount = 1` the call passes through to `_inner.CreateLock(name)` directly — byte-identical to today's Mutex behavior. A per-process `ConcurrentDictionary<(string, int), byte>` caches "slots held in this process" so sibling workers skip slots already held locally, reducing intra-process round-trips. Cross-process coordination remains via the underlying advisory lock. **Race window**: a slot freed during the linear scan may be missed; this is identical to Medallion's `SqlDistributedSemaphore` — a property of the trick, not the PG variant. Wait mode requeues immediately on miss and the next scan succeeds. Skip mode treats miss-equivalent-to-saturation as consistent with its semantics ("drop on contention").

**SQL Server implementation strategy.** Use Medallion's built-in `SqlDistributedSemaphore` (`SqlDistributedSynchronizationProvider.CreateSemaphore(name, maxCount)`) — it implements the same trick internally and is well-tested. ~10 LOC wrapper.

`ConcurrencyPipelineBehavior` calls the semaphore provider with the resolved limit. `ConcurrencyPublishBehavior` reads both `[Mutex]` and `[Semaphore]` attributes and populates the same metadata. Both attributes share the same lock-key prefix (`warp:concurrency:`).

**Lock-name construction is backend-specific** (each backend produces internally consistent results for itself; user-observable concurrency caps differ across backends when both attributes are used against the same key):

- **PostgreSQL.** `PostgresSemaphoreProvider`'s `maxCount = 1` fast path calls `_inner.CreateLock(name)` — bare base name, e.g. `warp:concurrency:k`. The `maxCount > 1` path iterates `_inner.CreateLock("{name}:{i}")` for `i ∈ [0, N)` — slot-keyed names, e.g. `warp:concurrency:k:0..k:4`. Result: `[Mutex("k")]` and `[Semaphore("k", N)]` use **disjoint** lock names. Combined concurrency for the same key on PG is `mutex_limit + semaphore_limit`.
- **SQL Server.** `SqlServerSemaphoreProvider` always delegates to `Medallion.Threading.SqlServer.SqlDistributedSemaphore.TryAcquireAsync` regardless of `maxCount`. Medallion's `SqlSemaphore` uses N distinct `sp_getapplock` names of the form `{name}0..{name}{N-1}` — including at `maxCount = 1` (which uses just `{name}0`). Result: `[Mutex("k")]` (acquires `k0`) and `[Semaphore("k", 5)]` (acquires one of `k0..k4`) **share the slot pool**. Combined concurrency for the same key on SQL Server is `max(mutex_limit, semaphore_limit)`, which in practice equals `semaphore_limit` since Mutex is always 1.

This asymmetry exists because Medallion does not expose Postgres semaphore support, so PG had to be implemented from scratch (the slot trick) while SQL Server reuses Medallion's pre-existing `SqlSemaphore`. Aligning the backends would require either (a) making PG also use slot-keyed names at `maxCount = 1` (breaks Mutex behavioral parity with the pre-rename state), or (b) replacing SQL Server's Medallion delegation with our own slot-trick implementation (substantial new code). Both are out of v1 scope.

**Practical guidance for users.** Pick *one* of `[Mutex]` or `[Semaphore]` against a given key — don't mix. If you mix on PG you get independent caps; if you mix on SQL Server they share the pool. The behavior is documented but using both against the same key is a footgun on either backend.

### Public API

```csharp
// Mutex — limit = 1, hard-coded. Default Mode = Skip.
[Mutex("payment-processing")]
public class ProcessPayment : IJob { }

[Mutex("user-handler", Mode = ConcurrencyMode.Wait)]
public class HandleTelegramUpdate : IJob { }

// Semaphore — limit > 1. Default Mode = Wait.
[Semaphore("payment-api", limit: 5)]
public class CallPaymentApi : IJob { }

[Semaphore("payment-api", limit: 5, Mode = ConcurrencyMode.Skip)]
public class DropOnFull : IJob { }

// Extensions
await publisher.Enqueue(new ProcessPayment(),
    new JobParameters().WithMutex("payment:123"));

await publisher.Enqueue(new ProcessPayment(),
    new JobParameters().WithMutex("payment:123", ConcurrencyMode.Wait));

await publisher.Enqueue(new CallPaymentApi(),
    new JobParameters().WithSemaphore("payment-api", limit: 5));

// Admin (runtime override)
public class ScalingService(IConcurrencyLimitManager limits)
{
    public Task ScaleUp(string key, int slots) =>
        limits.AddOrUpdateLimit(key, slots);
}

// Registration
builder.Services.AddWarpWorker<AppDbContext>(opt =>
{
    opt.UsePostgreSql();
    opt.AddConcurrency();   // registers behaviors, manager, entity
});
```

### `MutexAttribute` (limit-1-only)

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MutexAttribute : Attribute
{
    public MutexAttribute(string key) { Key = key; }

    public string Key { get; }

    public ConcurrencyMode Mode { get; init; } = ConcurrencyMode.Skip;
}
```

No `Limit` field. The publish behavior populates `meta.Limit = 1` when reading `[Mutex]`.

### `SemaphoreAttribute` (limit > 1)

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SemaphoreAttribute : Attribute
{
    public SemaphoreAttribute(string key, int limit) { Key = key; Limit = limit; }

    public string Key { get; }

    public int Limit { get; }

    public ConcurrencyMode Mode { get; init; } = ConcurrencyMode.Wait;
}
```

Default mode `Wait` — the unambiguous semaphore semantic ("queue surplus, don't drop").

### `IConcurrencyMetadata`

```csharp
public partial interface IConcurrencyMetadata : IJobMetadata
{
    string? ConcurrencyKey { get; set; }

    int? Limit { get; set; }

    ConcurrencyMode? Mode { get; set; }
}
```

### `WithMutex` / `WithSemaphore` extensions

```csharp
public static class ConcurrencyExtensions
{
    public static JobParameters WithMutex(
        this JobParameters parameters, string key, ConcurrencyMode mode = ConcurrencyMode.Skip)
    {
        parameters.Configure<IConcurrencyMetadata>(x =>
        {
            x.ConcurrencyKey = key;
            x.Limit = 1;
            x.Mode = mode;
        });

        return parameters;
    }

    public static JobParameters WithSemaphore(
        this JobParameters parameters, string key, int limit, ConcurrencyMode mode = ConcurrencyMode.Wait)
    {
        parameters.Configure<IConcurrencyMetadata>(x =>
        {
            x.ConcurrencyKey = key;
            x.Limit = limit;
            x.Mode = mode;
        });

        return parameters;
    }
}
```

### `ConcurrencyMode`

```csharp
public enum ConcurrencyMode
{
    Skip = 1,   // surplus → Deleted
    Wait = 2,   // surplus → Enqueued, ScheduleTime = now
}
```

### Pipeline behavior flow

```
ConcurrencyPipelineBehavior.HandleAsync:
  1. Bail-out: not IJob → next()
  2. meta = jobContext.GetMetadata<IConcurrencyMetadata>()
  3. Bail-out: meta.ConcurrencyKey == null → next()
  4. effectiveLimit = (await resolver.GetLimit(meta.ConcurrencyKey)) ?? meta.Limit ?? 1
  5. handle = await semaphoreProvider.TryAcquireAsync(
        $"warp:concurrency:{meta.ConcurrencyKey}",
        effectiveLimit,
        TimeSpan.Zero,
        ct)
  6. handle == null:
     - mode = meta.Mode ?? ConcurrencyMode.Skip
     - mode == Wait → outcome = { State = Enqueued, ScheduleTime = now,
                                  ClearHandlerType = true,
                                  LogMessage = $"Requeued — '{key}' full ({effectiveLimit} slots)" }
     - mode == Skip → outcome = { State = Deleted,
                                  LogMessage = $"Cancelled — '{key}' full ({effectiveLimit} slots)" }
  7. handle != null:
     try { return await next(); } finally { await handle.DisposeAsync(); }
```

The fallback chain `(admin row) ?? (meta.Limit) ?? 1` means: an admin override always wins; without an admin row, the attribute/extension limit is used; if neither is set, default to 1 (mutual exclusion).

### `ConcurrencyPublishBehavior`

Mirrors today's `MutexPublishBehavior` but reads either attribute. Caches per-type:

- If `[Mutex]` found and `meta.ConcurrencyKey == null` → set `Key`, `Limit = 1`, `Mode`.
- Else if `[Semaphore]` found → set `Key`, `Limit`, `Mode`.
- If both found → `[Mutex]` wins (registration order). Documented; should never happen in practice — adding both to one class is a contradiction.

### Admin layer (`ConcurrencyLimit` entity, `IConcurrencyLimitManager`)

```csharp
public class ConcurrencyLimit
{
    public string Name { get; set; } = string.Empty;

    public int Limit { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public interface IConcurrencyLimitManager
{
    Task AddOrUpdateLimit(string name, int limit, CancellationToken ct = default);

    Task<bool> RemoveLimit(string name, CancellationToken ct = default);

    Task<ConcurrencyLimitInfo?> GetLimit(string name, CancellationToken ct = default);

    Task<IReadOnlyList<ConcurrencyLimitInfo>> ListLimits(CancellationToken ct = default);
}

public record ConcurrencyLimitInfo(string Name, int Limit, DateTime UpdatedAt);
```

Entity contributed via `EntityConfigurators` from `AddConcurrency()` only when opted in (mirrors `AddCircuitBreakerStateEntity` at `src/core/Warp.Core/CircuitBreaker/CircuitBreakerServiceConfiguration.cs:27`).

`ConcurrencyLimitResolver` is scoped — caches admin-row lookups for the lifetime of one job execution scope. Cross-job staleness is intentional; admin updates take effect at the next pickup.

### Dashboard

`/warp/concurrency` — list / inline-edit / delete / add-new. Mirrors `CountersPage.tsx` (added in PR #159). 5s polling. Nav label "Concurrency".

Backend endpoints (added to `src/core/Warp.UI/Endpoints/WarpEndpoints.cs`):

```
GET    /warp/api/concurrency             → ListLimits()
GET    /warp/api/concurrency/{name}      → GetLimit(name)
POST   /warp/api/concurrency             → AddOrUpdateLimit  [body: { name, limit }]
PUT    /warp/api/concurrency/{name}      → AddOrUpdateLimit  [body: { limit }]
DELETE /warp/api/concurrency/{name}      → RemoveLimit
```

When `IConcurrencyLimitManager` is not registered (i.e. `AddConcurrency()` not called), endpoints return 404. The frontend probes `GET /api/concurrency` once on layout mount; on 404 the nav link is hidden.

### Counter (no new wiring)

`stats:requeued` is already emitted globally by the worker for any `Enqueued`/`Scheduled` outcome (PR #159). Wait mode requeues fall under this — Counters page surfaces the rate automatically for both `[Mutex]` and `[Semaphore]` jobs.

### Documentation

- `website/docs/features/mutex.md` (modify) → renamed conceptually as the canonical concurrency reference. Filename stays `mutex.md` for stable URL; H1 changes to "Concurrency control: Mutex and Semaphore". Adds a "Limit > 1: semaphore mode" section. Documents `ConcurrencyMode` and the namespace rename.
- `website/docs/features/semaphore.md` (new) — short page (~80 lines) about `[Semaphore]` ergonomics, default `Wait`. Cross-links to mutex.md.
- `website/docs/ui/concurrency-limits.md` (new) — dashboard page docs.
- `CLAUDE.md` (modify) — update architecture summary: "`[Mutex]` and `[Semaphore]` are the unified concurrency primitive. `Warp.Core.Concurrency` namespace. `IConcurrencyLimitManager` for runtime overrides."

## Out of scope

- **Strict release through `Failed`** (Hangfire's "strict mode"). Explicitly rejected, not deferred. Slots are bound to handler execution, not to the job's lifecycle: a failed handler releases its slot immediately, and the retry competes for a slot like any other job. Holding the slot across a retry's backoff window would leave the slot idle, reducing effective throughput below `Limit` — the opposite of what an operator setting the cap usually intends. The right model for almost every real semaphore use case ("cap concurrent calls to PaymentAPI to 5", "limit open DB connections to 5", "throttle CPU-heavy work to 5") is *cap currently-executing handlers*, not *cap living-jobs-in-the-system*. A characterization test (`HandlerThrows_SlotReleasesImmediately_SiblingCanAcquire`) locks in this behavior so a future refactor that accidentally introduced retain-through-retry semantics would be caught.
- **Format-string templating** in attribute keys.
- **Worker-fetch-level filtering** (Faktory-style). Violates §6.1.
- **Per-key configurable requeue delay/jitter.** v1.5 once telemetry exists.
- **FIFO / fairness.** Best-effort only.
- **Per-server limit overrides.** Global only.
- **Backwards-compatible aliases for the renamed types.** Engineers update import statements; user has explicitly accepted this.

## Risks

- **Migrating Mutex's lock provider to semaphore provider.** At `maxCount = 1` Medallion treats this as a distributed lock; behavior should be byte-identical. Risk: subtle semaphore-vs-lock implementation difference (release-on-disconnect timing). Mitigation: run all existing `MutexTests` + `MutexIntegrationTests` (renamed to live in the `Concurrency/` test folder) under the new provider.
- **Saturation churn.** Hangfire issue #1921 ping-pong. Mitigation: docs warn to monitor `requeued` rate via Counters and increase the limit if `requeued > succeeded`.
- **Slot leak on missed releases.** Mitigated by Medallion's connection-keepalive + Warp's stale-job recovery. No active reaper in v1.
- **Schema change for `ConcurrencyLimit` entity.** Warp does not ship migrations — users run `dotnet ef migrations add UpgradeWarp` against their own DbContext, which picks up the new entity via `WarpModelCustomizer`. Documented prominently in the upgrade notes for this version.
- **Linear-scan miss window (both backends).** Both implementations iterate `{name}:0`..`{name}:{N-1}` and acquire the first free slot. If a slot frees while we've already passed it, this acquire returns null even though a slot was technically free during the scan. Wait mode requeues immediately and the next scan succeeds — eventual success preserved. Skip mode drops the job, but Skip mode's semantics are already "drop on contention" so this is consistent. Documented in the user docs.
- **Admin precedence subtlety.** Documented prominently — admin row > attribute. Operator-edited limit is sticky across redeploys.
- **Rename churn.** Every test, doc, and call site that references `MutexMode`, `IMutexMetadata`, `Warp.Core.Mutex` must update. Mitigation: full test suite run after the rename batch catches missed updates.

## Verification

- All existing `MutexTests` pass under the renamed types and the new semaphore-provider path (limit=1 byte-identical).
- New tests for limit > 1: at limit=5, 10 same-key `[Semaphore]` jobs all complete with max-observed concurrency = 5.
- `[Semaphore("k", 1)]` and `[Mutex("k")]` exhibit identical contention behavior on the same key (proves shared primitive).
- Admin override: `AddOrUpdateLimit("k", 10)` then enqueue with `[Semaphore("k", 5)]` → effective limit is 10.
- Disjoint-namespace test: `[Mutex("k")]` and `[Semaphore("k", 1)]` use different lock-name namespaces (`k` vs `k:0`) and therefore do *not* share concurrency. Documented as expected.
- Dashboard E2E (Playwright): list, edit, delete a limit via the UI.
- Full test suite passes on both Postgres and SQL Server CI matrices.

## Scope classification

**Substantial feature.** Refactor + addition. Renames a recently-shipped namespace (cosmetic but touches every Mutex call site), adds a shared admin entity layer + dashboard. New public surface: `[Semaphore]`, `WithSemaphore`, `IConcurrencyLimitManager`, `ConcurrencyLimit` entity, `IWarpSemaphoreProvider`, `ConcurrencyMode` (renamed from `MutexMode`), `IConcurrencyMetadata` (renamed from `IMutexMetadata`). Subagent path applies (manifest > 6, security_impact = low).

## Implementation batches

1. **Namespace rename + slot-acquisition primitive.** Move `src/core/Warp.Core/Mutex/` → `src/core/Warp.Core/Concurrency/`. Rename types: `MutexMode → ConcurrencyMode`, `IMutexMetadata → IConcurrencyMetadata`, `MutexPipelineBehavior → ConcurrencyPipelineBehavior`, `MutexPublishBehavior → ConcurrencyPublishBehavior`, `MutexServiceConfiguration → ConcurrencyServiceConfiguration` (method `AddMutex` → `AddConcurrency`), `MutexExtensions → ConcurrencyExtensions`. Add `IWarpSemaphoreProvider` + Postgres/SQL Server impls + fake. Update lock-key prefix `warp:mutex:` → `warp:concurrency:`. **All existing Mutex tests must pass after the rename** (no behavior change beyond the provider swap).
2. **`[Semaphore]` attribute + `WithSemaphore` extension.** Add `SemaphoreAttribute`, extend `ConcurrencyExtensions` with `WithSemaphore`, extend `ConcurrencyPublishBehavior` to read `[Semaphore]` (in addition to `[Mutex]`). Add `Limit` to `IConcurrencyMetadata`. New tests: `SemaphoreAttribute_PropagatesKeyLimitAndModeWaitDefault`, `WithSemaphore_SetsAllFieldsInMetadata`.
3. **Pipeline behavior generalization.** Update `ConcurrencyPipelineBehavior` to call the semaphore provider with `meta.Limit ?? 1`. Tests for limit > 1: `Limit5_AcquiresFiveConcurrentJobs`, `Limit5_SixthJobRequeues_WaitMode`, `Limit5_SixthJobDeletes_SkipMode`, `MutexAndSemaphoreSameKey_ShareSlots` (proves shared primitive at storage level).
4. **`ConcurrencyLimit` entity + manager.** New entity, `AddConcurrencyLimitEntity`, EntityConfigurators wiring, `IConcurrencyLimitManager` + impl, EF migrations on both providers, manager tests.
5. **Precedence resolver.** `ConcurrencyLimitResolver` (scoped). Wire into `ConcurrencyPipelineBehavior`. Tests for admin override across both `[Mutex]` and `[Semaphore]` keys.
6. **Dashboard backend.** 5 endpoints under `/api/concurrency`. 404 when manager not registered. Endpoint tests.
7. **Dashboard frontend.** `ConcurrencyLimitsPage.tsx` mirroring `CountersPage.tsx`. Nav link with hide-on-404. Demo data. Vite rebuild → committed dist.
8. **Documentation.** Update `mutex.md`, add `semaphore.md`, add `concurrency-limits.md`, screenshots, update `CLAUDE.md`.

## Public contracts

Renamed (existing surface moves):

- `Warp.Core.Mutex.*` → `Warp.Core.Concurrency.*`
- `MutexMode` → `ConcurrencyMode`
- `IMutexMetadata` → `IConcurrencyMetadata`
- `MutexServiceConfiguration.AddMutex` → `ConcurrencyServiceConfiguration.AddConcurrency`

New surface (additive within `Warp.Core.Concurrency`):

- `SemaphoreAttribute`
- `ConcurrencyExtensions.WithSemaphore`
- `IConcurrencyLimitManager` + `ConcurrencyLimitInfo`
- `IWarpSemaphoreProvider`
- `Warp.Core.Data.Entities.ConcurrencyLimit`
- `Warp.Core.ServiceConfiguration.AddConcurrencyLimitEntity`
- `IConcurrencyMetadata.Limit` (new property on the renamed metadata interface)

## Open decisions

None.
