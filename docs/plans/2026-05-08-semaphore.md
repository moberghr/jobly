# Plan: Unified concurrency control — `[Mutex]` + `[Semaphore]` over a shared primitive

Spec: `docs/specs/2026-05-08-semaphore.md`. JSON sidecar: `docs/specs/2026-05-08-semaphore.json`. Brainstorm: `docs/specs/2026-05-08-semaphore-brainstorm.md`.

8 batches. Subagent path applies (manifest > 6, security_impact = low).

This is a **rename + generalization + addition**. Batch 1 is the most invasive (namespace + type renames touch every Mutex call site); subsequent batches build cleanly on the renamed surface. Breaking changes are explicitly accepted by the engineer, so no compat shims.

## Batch 1 — Namespace rename + slot-acquisition primitive

**Goal:** Move `Warp.Core.Mutex` to `Warp.Core.Concurrency`, rename types, add `IWarpSemaphoreProvider`, swap `MutexPipelineBehavior`'s lock provider for the semaphore provider at `maxCount = 1`. Behavior is unchanged from main.

**Files (renames):**
- `src/core/Warp.Core/Mutex/` directory → `src/core/Warp.Core/Concurrency/`
- `MutexAttribute.cs` — namespace rename only; `MutexAttribute` type stays (no `Limit` field added — it's limit=1 only)
- `MutexMode.cs` → `ConcurrencyMode.cs`; `MutexMode` → `ConcurrencyMode`
- `IMutexMetadata.cs` → `IConcurrencyMetadata.cs`; `IMutexMetadata` → `IConcurrencyMetadata`
- `MutexExtensions.cs` → `ConcurrencyExtensions.cs`; type renamed
- `MutexPipelineBehavior.cs` → `ConcurrencyPipelineBehavior.cs`; switch from `IWarpLockProvider` to `IWarpSemaphoreProvider`. At this batch the call uses hard-coded `maxCount = 1` (no `Limit` field on metadata yet).
- `MutexPublishBehavior.cs` → `ConcurrencyPublishBehavior.cs`
- `MutexServiceConfiguration.cs` → `ConcurrencyServiceConfiguration.cs`; method `AddMutex` → `AddConcurrency`
- Lock-key prefix in pipeline behavior: `warp:mutex:` → `warp:concurrency:`

**Files (new):**
- `src/core/Warp.Core/IWarpSemaphoreProvider.cs` — public interface; single async method `TryAcquireAsync(name, maxCount, timeout, ct) → IAsyncDisposable?`
- `src/core/providers/Warp.Provider.PostgreSql/PostgresSemaphoreProvider.cs` — N-distinct-named-locks trick. At `maxCount == 1` delegates to `_inner.CreateLock(name)` (byte-identical to current Mutex). At `maxCount > 1` iterates `{name}:0`..`{name}:{maxCount-1}` calling `_inner.CreateLock($"{name}:{i}").TryAcquireAsync(TimeSpan.Zero, ct)` until one succeeds. Per-process `ConcurrentDictionary<(string, int), byte>` skips slots known to be held locally. Wraps the returned handle to remove the cache entry on disposal. ~50 LOC.
- `src/core/providers/Warp.Provider.PostgreSql/PostgreSqlServiceConfiguration.cs` (modify) — register provider
- `src/core/providers/Warp.Provider.SqlServer/SqlServerSemaphoreProvider.cs` — wraps Medallion's `SqlDistributedSynchronizationProvider.CreateSemaphore(name, maxCount)`. ~15 LOC.
- `src/core/providers/Warp.Provider.SqlServer/SqlServerServiceConfiguration.cs` (modify) — register provider
- `src/tests/Warp.Tests/Fixtures/FakeSemaphoreProvider.cs` — in-memory; `HoldSlot(name, count)` to pre-saturate

**Tests (renames + updates):**
- `src/tests/Warp.Tests/Features/Mutex/` → `src/tests/Warp.Tests/Features/Concurrency/`
- All test files: `using Warp.Core.Mutex;` → `using Warp.Core.Concurrency;`
- All `MutexMode` references → `ConcurrencyMode`
- All `AddMutex` → `AddConcurrency`
- `WithMutex` keeps its name; lock key prefix in test assertions: `warp:mutex:` → `warp:concurrency:`

**Checkpoint:** `dotnet build Warp.slnx` passes. **All existing Mutex tests pass unchanged** under the renamed types and new provider — proves the limit=1 path is byte-identical.

**Risk:** any miss in the rename (forgotten `using` statement, missed `MutexMode` reference, missed call site) shows up as a build error. Errors are local and easy to fix.

## Batch 2 — `Limit` on metadata + `[Semaphore]` attribute + `WithSemaphore` extension

**Goal:** Add the limit field and the new user-facing surface. No pipeline-behavior change yet — pipeline still uses hard-coded 1.

**Files:**
- `src/core/Warp.Core/Concurrency/IConcurrencyMetadata.cs` (modify) — add `int? Limit { get; set; }`
- `src/core/Warp.Core/Concurrency/SemaphoreAttribute.cs` (new) — `[AttributeUsage(Class, Inherited=false)]`, ctor `(string key, int limit)`, `Mode` init-only (default `Wait`)
- `src/core/Warp.Core/Concurrency/ConcurrencyExtensions.cs` (modify) — add `WithSemaphore(parameters, key, limit, mode = Wait)`. Update existing `WithMutex` to set `meta.Limit = 1` explicitly (currently it doesn't set Limit since the field doesn't exist yet).
- `src/core/Warp.Core/Concurrency/ConcurrencyPublishBehavior.cs` (modify) — extend attribute-cache lookup to also recognize `SemaphoreAttribute`. When `[Semaphore]` is found and `meta.ConcurrencyKey == null`, populate `Key`, `Limit`, `Mode` from it. When `[Mutex]` is found, populate `Key`, `Limit = 1`, `Mode`.

**Tests:**
- `src/tests/Warp.Tests/TestData/Handlers/SemaphoreAttributeCommands.cs` (new) — minimal handlers with `[Semaphore]` for tests.
- `SemaphoreTests.cs` (new) under `Features/Concurrency/`:
  - `SemaphoreAttribute_PropagatesKeyLimitAndModeWaitDefault`
  - `WithSemaphore_SetsKeyLimitAndModeInMetadata`
- `MutexTests.cs` (extend) — `MutexAttribute_PopulatesLimitOneInMetadata`, `WithMutex_PopulatesLimitOneInMetadata`.

**Checkpoint:** Build + new tests pass. Existing tests unaffected (no behavior change).

## Batch 3 — Pipeline behavior reads `Limit`

**Goal:** Wire the metadata `Limit` field into the actual semaphore acquisition. This is where `[Semaphore("k", 5)]` actually starts behaving as N-slot.

**Files:**
- `src/core/Warp.Core/Concurrency/ConcurrencyPipelineBehavior.cs` (modify) — replace the hard-coded `1` with `meta.Limit ?? 1`. Update log messages to interpolate `effectiveLimit` instead of just the key.

**Tests (`SemaphoreTests.cs` extend):**
- `Limit5_AcquiresFiveConcurrentJobs` (integration)
- `Limit5_SixthJobRequeues_WaitMode`
- `Limit5_SixthJobDeletes_SkipMode`
- `MutexAndSemaphoreSameKey_ShareSlots` — `[Mutex("k")]` first, then `[Semaphore("k", 5)]` for the next jobs against the same key; assert that the Mutex job blocks the Semaphore jobs from acquiring slot 1 (proves shared primitive at storage level).
- Behavior ordering test: `[Mutex]` and `[Semaphore]` on the same class — `[Mutex]` wins.

**Checkpoint:** Build + all behavior tests pass on both backends. Integration test asserts max-observed concurrency = 5 across 10 same-key jobs.

**Risk:** Medallion semaphore-vs-lock parity. If the existing Mutex integration tests pass after batch 1's swap, this is already validated.

## Batch 4 — `ConcurrencyLimit` entity + manager

**Files:**
- `src/core/Warp.Core/Data/Entities/ConcurrencyLimit.cs` — `{ Name, Limit, UpdatedAt }`
- `src/core/Warp.Core/ServiceConfiguration.cs` (modify) — `AddConcurrencyLimitEntity(ModelBuilder, string?)`
- `src/core/Warp.Core/Concurrency/ConcurrencyServiceConfiguration.cs` (modify) — wire `EntityConfigurators.Add(ServiceConfiguration.AddConcurrencyLimitEntity)`
- `src/core/Warp.Core/Concurrency/IConcurrencyLimitManager.cs` + `ConcurrencyLimitInfo.cs` + `ConcurrencyLimitManager.cs`
- `src/tests/Warp.Tests/TestData/TestContext.cs` (modify) — call `AddConcurrencyLimitEntity`

**Migrations:** none in Warp's source tree. Warp does not ship migrations — users run `dotnet ef migrations add UpgradeWarp` against their own DbContext, which picks up the new entity via `WarpModelCustomizer`. Tests use `EnsureCreatedAsync` which creates the schema fresh; no migration files needed in the repo.

**Tests:** `ConcurrencyLimitManagerTests.cs` — upsert, get, list, remove on both backends.

**Checkpoint:** Build + manager tests pass on both backends. The new entity surfaces in `EnsureCreatedAsync` for both PG and SQL Server fixtures.

## Batch 5 — Precedence resolver

**Files:**
- `src/core/Warp.Core/Concurrency/ConcurrencyLimitResolver.cs` — scoped service. `Task<int?> GetLimit(string name, CancellationToken)` — checks admin row, caches per-scope.
- `src/core/Warp.Core/Concurrency/ConcurrencyPipelineBehavior.cs` (modify) — inject resolver. Replace `meta.Limit ?? 1` with `(await resolver.GetLimit(name)) ?? meta.Limit ?? 1`.
- `src/core/Warp.Core/Concurrency/ConcurrencyServiceConfiguration.cs` (modify) — register resolver scoped.

**Tests (extend `SemaphoreTests.cs`):**
- `AdminLimitOverridesAttributeLimit`
- `AdminLimitChangeTakesEffectOnNextPickup` (integration)
- `AdminLimitAppliesToBothMutexAndSemaphoreKeys`
- `MissingMetaLimitDefaultsToOne` — `[Mutex]` flow uses `Limit = 1` when no admin row exists (proves the fallback chain).

**Checkpoint:** Build + all behavior tests pass.

## Batch 6 — Dashboard backend

**Files:**
- `src/core/Warp.UI/Endpoints/WarpEndpoints.cs` (modify) — 5 endpoints under `/api/concurrency`
  - `GET concurrency` → `ListLimits()`
  - `GET concurrency/{name}` → `GetLimit(name)`
  - `POST concurrency` → upsert (body: name, limit)
  - `PUT concurrency/{name}` → upsert (body: limit)
  - `DELETE concurrency/{name}` → `RemoveLimit(name)`
- 404 when `IConcurrencyLimitManager` not registered

**Tests:** `ConcurrencyEndpointsTests.cs` — list / get / post / put / delete / 404. `WebApplicationFactory<Program>`.

**Checkpoint:** Build + endpoint tests pass.

## Batch 7 — Dashboard frontend

**Files:**
- `src/ui/src/types/index.ts` (modify) — `ConcurrencyLimitInfo`
- `src/ui/src/api/index.ts` (modify) — typed wrappers
- `src/ui/src/pages/concurrency/ConcurrencyLimitsPage.tsx` (new) — list, inline-edit, delete, add-new modal. Mirrors `CountersPage.tsx`. 5s polling.
- `src/ui/src/App.tsx` (modify) — route `/concurrency`
- `src/ui/src/layouts/MainLayout.tsx` (modify) — nav link "Concurrency" with hide-on-404
- `src/ui/src/demo/adapter.ts` + `src/ui/src/demo/data.ts` — sample data
- `src/ui/e2e/screenshots.spec.ts` (modify) — add page to screenshot list
- `npm run build` from `src/ui/` — regenerate `src/core/Warp.UI/dist/*` (committed)

**Checkpoint:** `npm run build` succeeds. Manual dev-server verification of CRUD flows.

## Batch 8 — Documentation

**Files:**
- `website/docs/features/mutex.md` (modify) — H1 → "Concurrency control: Mutex and Semaphore". Update namespace references throughout. Add "Limit > 1: semaphore mode" section. Document `ConcurrencyMode`.
- `website/docs/features/semaphore.md` (new, ~80 lines) — `[Semaphore]` ergonomics, default `Wait`, cross-link to mutex.md.
- `website/docs/ui/concurrency-limits.md` (new) — dashboard page docs.
- `website/static/img/screenshots/18-concurrency-limits.png` + `-dark.png` (via screenshot script)
- `CLAUDE.md` (modify) — update architecture summary: `Warp.Core.Concurrency` namespace; `[Mutex]` + `[Semaphore]` over shared primitive; `IConcurrencyLimitManager` for runtime overrides.

**Checkpoint:** `npm run build` in `website/` succeeds.

## Final pass

- Full test suite on both Postgres + SQL Server.
- Pre-commit review (security, secrets, performance, naming).
- Behavioral diff written.
- Spec-drift check vs JSON sidecar manifest.

## Review

Stage 1: `compliance-reviewer` — Critical issues fixed before stage 2.
Stage 2 parallel: `test-reviewer` + `architecture-reviewer`.
Max 3 iterations.

## Cleanup pass

`code-simplification` — dead code, redundant abstractions, premature factoring. If anything changes, rerun build + tests.

## Compound

Capture in `tasks/lessons.md`:
- Medallion semaphore-vs-lock parity (verified or surprising?).
- Generalizing a just-shipped feature (PR #159 → this rename was 1 day after ship).
- The dual-attribute pattern: when does it pay off vs single-attribute-with-param?
- Renaming a public namespace as part of a feature add — what process worked.

Propose `CLAUDE.md` updates if patterns emerged. Update `.claude/references/pre-commit-review-list.md` if review surfaced rules.
