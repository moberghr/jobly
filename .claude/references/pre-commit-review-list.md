# Pre-Commit Review Checklist — Warp

Fast checks before every commit. Skipped only on `git commit --no-verify` (don't).

## EF Core
- [ ] `AsNoTracking()` on read-only queries
- [ ] `.Select()` projection over `.Include()` for read paths
- [ ] No `_context.Set<>()` subqueries inside `.Select()` projections (§5.2)
- [ ] `CancellationToken` propagated through async EF calls

## Architecture
- [ ] No new logic in worker fetch/execute path (`WarpWorkerService`, `WarpDispatcherWorker`) — orchestration goes in `IServerTask` (§6.1, §2.3)
- [ ] No raw SQL outside `Warp.Provider.PostgreSql` / `Warp.Provider.SqlServer` (§5.1)
- [ ] No `DateTime.UtcNow` in production code — use injected `TimeProvider` (§5.7)
- [ ] No `IServiceProvider` injection — inject specific deps or `IServiceScopeFactory` (§2.4)
- [ ] No `InternalsVisibleTo` to reach Core internals from addons (§2.11)

## Addons & Composition
- [ ] If both `AddRetry()` and `AddTimeout()` are registered, retry comes first (§2.12)
- [ ] If both `AddConcurrency()` and `AddRateLimit()` are registered, concurrency comes first (§2.12)
- [ ] No PII in `[Mutex]` / `[Semaphore]` / `[RateLimit]` keys — they appear in logs and the dashboard (§1.2)

## Tests
- [ ] New public method has a test on both Postgres and SQL Server (`[GenerateDatabaseTests]` source generator)
- [ ] Each unit test calls one public method on one class (§4.8)
- [ ] No `Task.Delay` in tests except for handlers designed to be cancelled (§4.5)
- [ ] No raised `[TimedFact]` budget to mask a flake (§4.4)
- [ ] No spray-N concurrency tests — use `BarrierSignal` with N=2 (§4.7)

## Misc
- [ ] No hardcoded secrets / connection strings (§1.1)
- [ ] No PII in log output above `LogTrace` (§1.2)
- [ ] Build passes with zero warnings (`TreatWarningsAsErrors=true`, §7.3)
- [ ] Enum values explicitly assigned starting at 1 (§8.11)
- [ ] Metadata properties addon-prefixed (`RateLimitKey`, not `Key`) (§8.12)
