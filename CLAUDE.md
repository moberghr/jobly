# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Warp

Warp is a distributed job processing and message queue library for .NET 10. Four patterns:

- **Messages** (`IMessage`) — Pub/sub queue. Multiple handlers per message, each an independent job.
- **Jobs** (`IJob`) — Orchestrated background work. Single handler, scheduling, retries, continuations, batches, named queues.
- **Requests** (`IRequest<TResponse>`) — In-memory request/response. Single handler, no database persistence, returns typed response immediately via `IMediator.Send()`.
- **Streams** (`IStreamRequest<TResponse>`) — In-memory streaming. Single handler, no database persistence, returns `IAsyncEnumerable<TResponse>` via `IMediator.CreateStream()`.

**Optional DB push**: `opt.UseDatabasePush()` on the builder replaces polling wake-up with push notifications (Postgres LISTEN/NOTIFY, SQL Server Service Broker) for the dispatcher, `MessageRouter`, and `Orchestrator`. Worker fetch push requires `UseDispatcher = true` — individual-worker mode has a thundering-herd problem and is left on polling. See §2.9 and the DB Push section in `README.md`.

Ships as NuGet packages: `Warp.Core` (provider-agnostic), `Warp.Provider.PostgreSql` (PG-specific), `Warp.Provider.SqlServer` (SQL Server-specific), `Warp.Worker` (worker runtime), `Warp.UI` (dashboard), `Warp.Http` (optional — HTTP exposure for `IRequest<T>` / `IStreamRequest<T>` handlers). Users install the provider package that matches their database and call `opt.UsePostgreSql()` or `opt.UseSqlServer()` inside the `AddWarp` / `AddWarpWorker` lambda.

## Build & Test Commands

```bash
# Backend (from src/)
dotnet build Warp.slnx
dotnet test --project tests/Warp.Tests/Warp.Tests.csproj                                          # Full suite (1,024 tests, ~1m 30s)

# By database requirement (CI uses this matrix)
dotnet test --project tests/Warp.Tests/Warp.Tests.csproj -- --filter-trait "Category=NoDb"        # Pure-unit, no container (~3s)
dotnet test --project tests/Warp.Tests/Warp.Tests.csproj -- --filter-trait "Category=PostgreSql"  # PG-backed (~1m 10s)
dotnet test --project tests/Warp.Tests/Warp.Tests.csproj -- --filter-trait "Category=SqlServer"   # SQL Server-backed (~1m 20s)

# Run specific test suites
dotnet test --project tests/Warp.Tests/Warp.Tests.csproj -- --filter-namespace "Warp.Tests.Unit"         # Unit tests only
dotnet test --project tests/Warp.Tests/Warp.Tests.csproj -- --filter-namespace "Warp.Tests.Integration"  # Integration tests only

# Frontend (from src/ui/)
npm install
npm run dev      # Vite dev server on localhost:5173 (proxies /api to localhost:5000)
npm run build    # Production build
```

## Architecture

### Unified Data Model

Everything is a **Job** with a `Kind` discriminator enum (`Job=1, Message=2, Batch=3`). Messages and Batches are Jobs that spawn/group child jobs. The `ParentJobId` chain handles all relationships — no separate Message or Batch tables.

```csharp
publisher.Publish(new OrderCreated());                    // IMessage → Kind=Message job, routed to N handlers
publisher.Enqueue(new SendReport());                      // IJob → Kind=Job, single handler
publisher.Schedule(new SendReport(), tomorrow);           // IJob → scheduled
publisher.Enqueue(new Task(), queue: "critical");         // Named queue
publisher.Enqueue(new FollowUp(), parentJobId: id);       // Continuation
batchPublisher.StartNew(jobs);                            // Kind=Batch job, children linked via ParentJobId
var user = await mediator.Send(new GetUser(id));          // IRequest<User> → in-memory, returns User
await foreach (var item in mediator.CreateStream(new GetItems())) { } // IStreamRequest<Item> → in-memory, streams items
```

### Handler Interfaces

```csharp
public interface IMessageHandler<in T> where T : IMessage { Task HandleAsync(T message, CancellationToken ct); }
public interface IJobHandler<in T> where T : IJob { Task HandleAsync(T message, CancellationToken ct); }
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { Task<TResponse> HandleAsync(TRequest request, CancellationToken ct); }
public interface IStreamRequestHandler<in TRequest, out TResponse> where TRequest : IStreamRequest<TResponse> { IAsyncEnumerable<TResponse> HandleAsync(TRequest request, CancellationToken ct); }
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse> { Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct); }
public interface IStreamPipelineBehavior<TRequest, TResponse> where TRequest : IStreamRequest<TResponse> { IAsyncEnumerable<TResponse> HandleAsync(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken ct); }
```

### Type Hierarchy

All types implement `IRequest<TResponse>`. `IStreamRequest<TResponse>` extends `IRequest<IAsyncEnumerable<TResponse>>` — streams are requests whose response is an async enumerable.

```csharp
public interface IRequest<out TResponse>;                                        // Base — all types implement this
public interface IJob : IRequest<Unit>;                                          // Persistent, single handler
public interface IMessage : IRequest<Unit>;                                      // Persistent, multiple handlers
// IRequest<TResponse> used directly for in-memory                              // In-memory, returns TResponse
public interface IStreamRequest<out TResponse> : IRequest<IAsyncEnumerable<TResponse>>; // In-memory, streams TResponse
```

### In-Memory Requests (IMediator)

Requests are NOT persisted to the database. They execute immediately in-process and return a typed response. Resolved via `IMediator.Send()`. Go through the same unified `IPipelineBehavior<TRequest, TResponse>` pipeline as jobs and messages.

```csharp
public class GetUser : IRequest<UserDto> { public int Id { get; set; } }

public class GetUserHandler : IRequestHandler<GetUser, UserDto>
{
    public async Task<UserDto> HandleAsync(GetUser request, CancellationToken ct)
    {
        return await _db.Users.FindAsync(request.Id, ct);
    }
}

// Usage:
var user = await mediator.Send(new GetUser { Id = 1 });
```

### In-Memory Streams (IMediator)

Streams are NOT persisted to the database. They execute immediately in-process and return an async enumerable. Resolved via `IMediator.CreateStream()`. Since `IStreamRequest<T>` extends `IRequest<IAsyncEnumerable<T>>`, request-level `IPipelineBehavior` applies automatically (auth, logging, counting). For enumeration-level concerns (timing, per-item transforms), use `IStreamPipelineBehavior<TRequest, TResponse>`.

```csharp
public class GetUsers : IStreamRequest<UserDto> { public string Role { get; set; } }

public class GetUsersHandler : IStreamRequestHandler<GetUsers, UserDto>
{
    public async IAsyncEnumerable<UserDto> HandleAsync(GetUsers request, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var user in _db.Users.Where(x => x.Role == request.Role).AsAsyncEnumerable().WithCancellation(ct))
        {
            yield return new UserDto { Id = user.Id, Name = user.Name };
        }
    }
}

// Usage:
await foreach (var user in mediator.CreateStream(new GetUsers { Role = "Admin" }))
{
    Console.WriteLine(user.Name);
}
```

### Job Entity

Single table with `Kind` enum. Key fields:
- **Shared**: Id, Kind, Type, Message (payload), CreateTime, CurrentState, Queue, ExpireAt, ParentJobId, TraceId, SpawnedByJobId, CancellationMode
- **Job-specific**: ScheduleTime, HandlerType, CurrentWorkerId, LastKeepAlive
- **Batch-specific**: ContinuationOptions (generalized to all kinds — controls child activation on parent failure)
- **JobLog** — Unified audit trail. EventType: Created, Processing, Completed, Failed, Requeued, Deleted, Cancelled, CancellationRequested, Log (ILogger output). Includes optional `WorkerId` (set by worker-produced entries, null for command/orchestration entries).

### Worker Architecture

Workers are **pure executors** — they only fetch and execute `Kind=Job` jobs. All orchestration is handled by scoped `IServerTask` services driven by a single `ServerTaskHost<TContext>` (see §2.2 / §2.3):

```
Worker (pure executor):
  1. Fetch Job (Kind=Job, Enqueued, ScheduleTime < now)
  2. Execute handler (pipeline behaviors run first — mutex, retry, etc.)
  3. Update state, counters, logs
  4. ServerTaskSignals.SignalJobFinalized() — wakes Orchestrator

MessageRouter (polls every 1s, signalled on MessageEnqueued):
  - Routes Kind=Message jobs → discovers handlers, creates N child jobs

Orchestrator (polls every 10s, signalled on JobFinalized):
  - Finalizes parents when all children reach terminal state
  - Activates continuations (Awaiting children of completed parents)
```

Every task implements `IServerTask` (`Name`, `LockKey`, `DefaultInterval`, `ExecuteAsync`, optional `RerunImmediately` / `LogOnSuccess` defaults). Tasks are registered as scoped services; the host resolves one per iteration, takes the distributed lock if `LockKey != null`, runs `ExecuteAsync`, and writes `ServerTask` / `ServerLog` rows. Nullable `DefaultInterval` lets an operator disable a task entirely.

### Job Cancellation

Uses `CancellationMode` enum (`None=0, Graceful=1`) instead of immediate state change. When a processing job is cancelled:
1. `DeleteJob` sets `CancellationMode = Graceful` (state stays `Processing`, no `ExpireAt` yet)
2. `RunJobMonitor` detects `CancellationMode != None` and cancels the handler's `CancellationToken`
3. If handler respects the token: worker sets state to `Deleted`, `ExpireAt`, clears `CancellationMode`
4. If handler ignores the token and completes: state is `Completed` (work happened), `CancellationMode` cleared
5. Job stays in Processing tab with "Cancelling..." badge until handler actually exits

### Job Timeout

Opt-in addon via `opt.AddTimeout()`. Single primitive in `Warp.Core.Timeout`: `[Timeout(seconds: N)]` attribute, `WithTimeout(TimeSpan)` extension, optional fleet-wide `o.Default`. `TimeoutPipelineBehavior` wraps `next()` with `new CancellationTokenSource(delay, TimeProvider)` linked with the worker's cancellation token. On timer fire, two modes via `TimeoutMode`: `Delete` (default — pipeline sets `JobOutcome { State = Deleted, LogMessage = "Timed out after Xs" }`; NOT retried by `AddRetry`) and `Fail` (throws `TimeoutException`; `AddRetry` catches it like any other exception). Two scopes via `TimeoutScope`: `PerAttempt` (default — each retry gets its own fresh budget) and `Total` (publish behaviour stamps `DeadlineUtc = CreateTime + TimeoutSeconds`; past-deadline attempts fire immediately). Cooperative cancellation only — same `Thread.Abort` constraint as `DeleteJob`. Pipeline ordering: `AddRetry()` MUST be registered BEFORE `AddTimeout()` so retry's catch can see Fail-mode's `TimeoutException`. No new entity, no schema change. v1 ships the audit-log entry only — a `stats:timeout` counter is v1.1 follow-up (requires worker-side wiring to avoid a fresh DbContext scope per timeout fire).

### Concurrency Control (Mutex + Semaphore)

Opt-in addon via `opt.AddConcurrency()` on the builder. Single primitive in `Warp.Core.Concurrency` exposed as two attributes: `[Mutex("k")]` (limit fixed at 1) and `[Semaphore("k", N)]` (limit > 1). Both share the same `ConcurrencyPipelineBehavior`, which calls `IWarpSemaphoreProvider.TryAcquireAsync($"warp:concurrency:{key}", limit, TimeSpan.Zero, ct)` and short-circuits to Deleted (Skip mode) or Enqueued (Wait mode) via `IJobContext.Outcome` if no slot is free. Lock released after handler completes. Zero overhead for jobs without a key. Concurrency key + limit + mode stored in job metadata. Defaults: `[Mutex]` is `Skip`, `[Semaphore]` is `Wait`. `[Mutex("k")]` and `[Semaphore("k", N)]` use disjoint lock-name namespaces (`warp:concurrency:k` vs `warp:concurrency:k:0..k:{N-1}`) by design — pick one or the other for a given key. Admin overrides via `IConcurrencyLimitManager.AddOrUpdateLimit("key", N)` — runtime-editable from the dashboard at `/warp/concurrency`, persisted in the `ConcurrencyLimit` entity, and take precedence over attribute limits (admin row > attribute > 1).

### Rate Limiting (Windowed)

Opt-in addon via `opt.AddRateLimit()` on the builder. Bounds executions per key per time window. Two attribute styles via `Style` parameter: `Fixed` (default — wall-clock window floor-aligned to global UTC tick boundaries, cheaper, allows boundary bursts up to 2× limit, two keys with the same window roll over at identical wall-clock moments) and `Sliding` (rolling window over the last N starts as JSON-encoded ticks, defensively trimmed to `count` on read so corrupted state self-heals). Two outcome policies via `Mode`: `Skip` (default — surplus jobs short-circuit to `Deleted` with a "Cancelled — rate limit exceeded" log) and `Wait` (surplus jobs reschedule via `JobOutcome.RescheduledState` to the next available slot, audit log shows `Throttled — rescheduled to <iso>`). Apply with `[RateLimit("key", count: 100, perSeconds: 60)]` on `IJob` classes or `parameters.WithRateLimit("key", count, window, mode, style)` per-publish. `perSeconds` capped at `RateLimitAttribute.MaxWindowSeconds` (7 days) to prevent `DateTime` overflow; admin manager enforces the same cap. `RateLimitPipelineBehavior` acquires `IWarpLockProvider.TryAcquireAsync("warp:ratelimit:{key}", 5s)` briefly for the check-and-increment, then releases — the lock is **never** held during handler execution (unlike Mutex/Semaphore). Lock-contention timeout reschedules with 100–500ms randomised jitter to avoid thundering-herd on hot keys. Live state in `RateLimitBucket` entity (`Name`, `WindowStartUtc`, `CurrentCount`, `TimestampsJson`, `UpdatedAt`) — persisted across worker sessions because window counts must outlive any single connection. Admin overrides via `IRateLimitManager.AddOrUpdateLimit("key", count, windowSeconds)` — runtime-editable from `/warp/ratelimits`, persisted in the `RateLimitOverride` entity, precedence `admin row > attribute/metadata`. Resolved via scoped `RateLimitResolver` (cached per scope). Emits a `warp.rate_limit_check` OTel span per evaluation with `warp.rate_limit.{key,count,window_seconds,style,outcome}` tags (outcomes: `acquired`, `skipped`, `throttled`, `lock_contention`). **Keys appear in `JobLog.Message` and the dashboard** — don't put PII in the key (use `tenant-bucket-A`, not `payment:{customerEmail}`); hash or tokenise upstream if the key needs to carry tenant identity. **DB-push does NOT accelerate Wait-mode reschedules** — they land in `State.Scheduled` and depend on `ScheduledJobActivation` polling at `ScheduledActivationInterval` (default 5s) for pickup eligibility. **Composition order** when both `[Mutex]` and `[RateLimit]` apply to the same job: register `AddConcurrency()` *before* `AddRateLimit()` so the mutex check runs outermost; this preserves rate-limit tokens when the mutex rejects. Hide-on-404 nav probe, same as Concurrency.

### Recurring Jobs

- `AddOrUpdateRecurringJob` only registers/updates the definition (cron, message, type). **Does not create jobs.** Acquires a distributed lock on the recurring job name, saves immediately (exception to §5.7 — lock must be held during save to prevent race conditions).
- `RecurringJobScheduler` creates jobs with `ScheduleTime = now` (ready for immediate execution) and sets `NextExecution` to the next cron occurrence.
- **RecurringJobLog** — Immutable audit trail linking recurring jobs to their created jobs. Fields: `Id, RecurringJobId, JobId (nullable), CreatedAt`. `JobId` has FK with `SET NULL` cascade — when the job is cleaned up, `JobId` becomes null but the log entry survives. Navigation property `Job` for clean LINQ queries.
- Scheduler uses RecurringJobLog for dedup: checks if the most recent log entry's job is still Enqueued/Processing via nav property.
- ExpirationCleanup retains last 100 logs per recurring job (uses `HAVING COUNT > 100` to skip most).

### Dashboard Authorization

Two modes:
- **Built-in login**: `options.UseBuiltInLogin<TValidator>()` — Warp serves a React login page, manages HTTP-only signed cookie (7 day expiry via ASP.NET Data Protection). Register `IWarpCredentialValidator` in DI (scoped, can inject DbContext). Login/logout via `/api/auth/login` and `/api/auth/logout`. SPA catches 401 from Axios interceptor and shows login component.
- **Custom redirect**: `options.Authorization = new MyFilter()` + `options.UnauthorizedRedirectUrl = "/login"` — your app handles login. Filter checks `HttpContext` (claims, roles, etc.). API gets 401, SPA gets 302 redirect.

Default: no auth (open access).

### Key Design Decisions

- **No raw SQL** — all queries use EF Core LINQ. No `_context.Set<>()` subqueries inside `.Select()` projections — use navigation properties or two-step fetch instead.
- **Naming conventions respected** — entity configurations do NOT use `.ToTable()`, so EF Core naming conventions (e.g., `UseSnakeCaseNamingConvention()`) can transform table/column names freely. Schema is set via `entity.Metadata.SetSchema()` to avoid re-pinning table names.
- **Default schema** — All Warp tables default to the `"warp"` schema. Configurable via `WarpConfiguration.Schema`. Set to `null` for the database's default schema.
- **TimeProvider** — all production code uses injectable `TimeProvider` instead of `DateTime.UtcNow`. Registered as `TryAddSingleton(TimeProvider.System)` in `AddWarp`. Test code can use `DateTime.UtcNow` directly.
- **DbContext must be registered as Scoped** (not Transient). The outbox pattern requires the publisher and application code to share the same DbContext instance within a scope.
- Everything is a Job. `ParentJobId` replaces both the old MessageId and BatchId foreign keys.
- Workers never touch parent/child orchestration — that's the `Orchestrator`'s job.
- `ContinuationOptions` is generalized: any job with children can control whether children activate on failure.
- Failed jobs never auto-deleted.
- Statistics use Counter rows (write-optimized) aggregated into Statistic rows by `CounterAggregator`.
- Every background task implements `IServerTask` and is driven by the single `ServerTaskHost<TContext>`. Signal-driven wake-up (`ServerTaskSignals<TContext>`) wakes `Orchestrator` on `JobFinalized` and `MessageRouter` on `MessageEnqueued`; the other tasks are time-driven.
- `RequeueJob` resets `ScheduleTime` to now — requeued jobs always execute immediately.
- Count-based cleanup: `MaxExpirableJobCount` (default null/disabled) — deletes oldest by `ExpireAt` when threshold exceeded. Failed jobs excluded (null `ExpireAt`).
- `JobExpirationTimeout` configurable on base `WarpConfiguration` (default 1 day). Used by worker, command service, and orchestration task.
- Job metadata: `JobParameters.Metadata` (key-value dictionary), stored as JSON on the Job entity. `IJobContext` gives handlers access to metadata, job ID, and trace ID. `IPublishPipelineBehavior<T>` intercepts publish for cross-cutting metadata. Metadata inherited by child jobs via ambient context.
- Pause/Resume: Server and worker group level. `PausedAt` timestamp on Server and WorkerGroup entities. `PauseStateHolder` (in-memory snapshot) updated by `Heartbeat`, checked by workers before each poll. **Not instantaneous** — `PauseServer`/`PauseWorkerGroup` only writes `PausedAt`; each server's worker pool keeps fetching until that server's next `Heartbeat` tick refreshes the holder (cadence `HealthCheckInterval`, default 3s), and an in-flight worker iteration that already passed its pause check will still complete its current claim. Treat pause as "no new fetches after up to one heartbeat", not as a synchronous barrier — callers needing hard quiesce semantics combine pause with a wait of at least `HealthCheckInterval + PollingInterval`. Tests that need deterministic timing configure `HealthCheckInterval = null` and call `WarpTestServer.RunHeartbeatOnceAsync` to drive the holder flip explicitly. API endpoints: `POST /api/servers/{id}/pause|resume`, `POST /api/groups/{id}/pause|resume`.
- Real-time log flushing: `RunJobMonitor` drains `JobLogCollector` every ~1s during handler execution and persists logs to the database. Logs visible in dashboard while the job is still processing.

### Backend (.NET 10)

- **Warp.Core** — Entities (Job, RecurringJob, RecurringJobLog, JobLog, Server, Worker, ServerTask, ServerLog), handlers, JobDispatcher (cached reflection), Publisher, BatchPublisher, logging (JobLogContext/JobLoggerProvider). Services: `JobQueryService`, `JobCommandService`, `JobGroupQueryService`, `RecurringJobService`, `DashboardStatsService`.
- **Warp.Worker** — WarpWorkerService (pure executor), WarpDispatcher/WarpDispatcherWorker (batch-fetch mode), worker groups. Server-task services driven by `ServerTaskHost<TContext>` (all implement `IServerTask`): `Heartbeat`, `CounterAggregator`, `ServerCleanup`, `StaleJobRecovery`, `ExpirationCleanup`, `RecurringJobScheduler`, `ScheduledJobActivation`, `MessageRouter`, `Orchestrator`. Push → wake plumbing: `ServerTaskSignals<TContext>`.
- **Warp.UI** — Minimal API endpoints + embedded SPA served at `/warp`. Auth middleware (`IWarpAuthorizationFilter`, `IWarpCredentialValidator`, built-in cookie login). Typed `config.ts` for window globals.
- **Warp.Http** (optional) — Exposes `IRequest<T>` / `IStreamRequest<T>` handlers as ASP.NET Minimal API endpoints. Annotate the **handler class** with `[WarpHttpGet/Post/Put/Patch/Delete("/route")]`; source generator (`Warp.Http.SourceGenerator`) emits a `RequestDelegate` per endpoint. Binding delegated to ASP.NET Minimal API — full `[FromRoute]`/`[FromQuery]`/`[FromHeader]`/`[FromBody]`/`[AsParameters]` support. `IJob` / `IMessage` rejected at compile time (`WHTTP001`); use a thin `IRequest<Guid>` wrapper that calls `IPublisher.Enqueue` for "submit a job via HTTP". Independent of `Warp.UI`.
- **Static analyzers** — StyleCop, Roslynator, SonarAnalyzer, Meziantou.

### Frontend (Vite + React 18 + TypeScript)

`src/ui/`. Tailwind + shadcn/ui, Zustand, Axios (with 401 interceptor). Dashboard with clickable metric cards, realtime + historical graphs, dark mode. Job list by state with bulk actions. Failed jobs type filter with bulk delete/requeue by type. Job detail with colored state cards, handler output, "Cancelling..." badge, mutex key. Batch progress bar (stacked green/red). Recurring job execution history with "Cleaned up" for deleted jobs. Worker detail page with activity log. Built-in login page component. Logout button in navbar (when built-in login active).

## Testing

1,024 tests (135 NoDb + 445 PostgreSQL + 444 SQL Server) in `src/tests/Warp.Tests/`, using xUnit v3, Shouldly, Testcontainers + Respawn. Full suite ~1m 30s locally; per-category ~3s / ~1m 10s / ~1m 20s.

Tests are organized by **feature folder** (`Admin/`, `Core/`, `Features/Retry/`, `Worker/`, `Notifications/`, etc.), not by unit-vs-integration split. The `[GenerateDatabaseTests(FixtureKind.X)]` source generator (`src/tests/Warp.Tests.SourceGenerator/`) emits `_PostgreSql` and `_SqlServer` concrete subclasses from a single abstract base, so each behavior is asserted on both backends.

### Test Categories (xUnit traits)

**NoDb** — ~135 tests, ~3s. Pure unit tests: no container, no DB, no fixture. Things like `PollingBackoffTests`, `CompletionBatchTests`, `MetadataSerializerTests`, `DashboardAuthTests` (uses `Microsoft.AspNetCore.TestHost`).

**PostgreSql** / **SqlServer** — ~445 / ~444 tests each. Any test that needs a real database. Two sub-styles:

1. **Unit-style against a real DB** (most tests): each test calls exactly ONE public method on ONE class. State set up via direct DB inserts. Fresh `CreateContext()` for arrange / act / assert — no shared change tracking. Uses `[GenerateDatabaseTests(FixtureKind.Default)]` → `PostgreSqlFixture` / `SqlServerFixture` with Respawn between tests.

2. **Integration via `WarpTestServer`**: boots full worker + all background tasks against a real database. Tests publish jobs and wait for completion via `Server.WaitForCompletion()` / `Server.WaitForJobState()`. Uses `[GenerateDatabaseTests(FixtureKind.Integration)]` → `PostgreSqlIntegrationFixture` / `SqlServerIntegrationFixture` (server boots once per fixture). Variants: `FixtureKind.BatchedCompletion` for dispatcher-mode batching; `FixtureKind.MultiServer` for 2-server distributed-coordination tests.

### TimedFact default

Every test-affecting attribute defaults to **10s** (`[TimedFact]`, `[TimedTheory]`). Tests exercising genuinely slow behavior (retry chains with real delays, end-to-end workload tests) opt in explicitly with `[TimedFact(N_000)]`. A short default surfaces deadlocks and hangs immediately instead of hiding them behind a half-minute wait. See `src/tests/Warp.Tests/TestData/TimedFactAttribute.cs`.

### Writing Unit Tests

Only the abstract base is hand-written. The source generator emits `_PostgreSql` and `_SqlServer` subclasses with the right fixture, collection, and `Category` trait.

```csharp
[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class MyTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    protected MyTestsBase(IDatabaseFixture fixture) => _fixture = fixture;
    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task MethodName_Scenario_ExpectedResult()
    {
        // Arrange: insert state directly into DB
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job { Id = jobId, Kind = JobKind.Job, CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow, ScheduleTime = DateTime.UtcNow, Queue = "default" });
        await ctx.SaveChangesAsync();

        // Act: call ONE method on ONE class
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new WarpConfiguration()));
        await svc.DeleteJob(jobId);

        // Assert: query DB for result
        var job = await _fixture.CreateContext().Set<Job>().FindAsync(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }
}
```

### Writing Integration Tests

```csharp
[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class MyIntegrationTestsBase : IntegrationTestBase
{
    protected MyIntegrationTestsBase(IDatabaseFixture fixture) : base(fixture) { }

    [TimedFact]
    public async Task GivenWorkload_WhenProcessed_ThenExpectedResult()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new MyRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }
}
```

Use `FixtureKind.BatchedCompletion` for dispatcher-mode completion-batch tests, `FixtureKind.MultiServer` for two-server distributed-coordination tests. A test that needs a one-off custom worker config can call `WarpTestServer.StartAsync(_fixture, configure: cfg => ...)` inline, as the durability and batched-completion tests do.

### Test Principles

- Each test tests ONE public method. No chaining multiple calls to simulate flows.
- If testing a multi-step flow (worker + orchestration + routing), use integration tests with `WarpTestServer`.
- No `Task.Delay` in tests except for handlers meant to be cancelled (`CancellableCommand`).
- No unnecessary abstractions — test handlers are simple (empty, throw, increment counter).
- Tests run on both PostgreSQL and SQL Server via abstract base + concrete subclasses.
- Cancel long-running jobs at end of integration tests so they don't block (~600ms vs 30s).

### Registration

`AddWarp<TContext>(opt => ...)` / `AddWarpWorker<TContext>(opt => ...)` take a single `Action<WarpBuilder<TContext>>` / `Action<WarpWorkerBuilder<TContext>>` lambda and automatically configure the user's DbContext:
- Wraps the existing `DbContextOptions<TContext>` service descriptor to add row-lock interceptors
- Replaces `IModelCustomizer` with `WarpModelCustomizer` to auto-apply entity configurations
- Registers `TimeProvider.System` via `TryAddSingleton` (overridable for testing)
- Registers `IWarpLockProvider` (wraps `IDistributedLockProvider` — Medallion.Threading is internal; the concrete `IDistributedLockProvider` is registered by the provider package)
- Users just register their DbContext normally — no manual configuration needed
- The builder inherits from `WarpConfiguration` / `WarpWorkerConfiguration`, so config fields (`WorkerCount`, `PollingInterval`, `DefaultQueue`, etc.) are set directly on `opt`
- Opt-in addons on the builder: `opt.AddRetry()`, `opt.AddConcurrency()` (Mutex + Semaphore), `opt.AddRateLimit()`, `opt.AddCircuitBreaker()`, `opt.AddNoRestart()`, `opt.AddTimeout()`, `opt.UseDatabasePush()`. **Ordering**: when both `AddRetry()` and `AddTimeout()` are registered, `AddRetry()` MUST come first — DI insertion order = outer → inner, so retry needs to wrap timeout for its `catch (Exception)` to see the `TimeoutException` thrown by Timeout's `Fail` mode.
- Provider selection via `opt.UsePostgreSql()` / `opt.UseSqlServer()` (from `Warp.Provider.PostgreSql` / `Warp.Provider.SqlServer`) — mandatory for real use; registers `IWarpSqlQueries`, `IDatabaseExceptionClassifier`, `IWarpNotificationTransportFactory`, and the provider-specific `IDistributedLockProvider`

### Worker Groups

Workers can be split into groups with independent queues and polling intervals. Top-level `WorkerCount`/`Queues`/`PollingInterval` become the first implicit group. `AddWorkerGroup()` adds additional groups. Default `WorkerCount = Math.Min(Environment.ProcessorCount * 5, 20)`.

---

## Engineering Standards

> Added by moberg-init on 2026-04-09. Based on:
> - Moberg HR coding guidelines (`.claude/references/coding-guidelines.md`)
> - Architecture principles (`.claude/references/architecture-principles.md`)
> - Codebase scan of this repository
>
> Numbered rules (§X.Y) are referenced by the compliance reviewer.

### §1 — Security & Data Integrity

- **§1.1** No secrets, connection strings, or credentials in source code. Use `appsettings.Development.json` (gitignored) or environment variables for local dev.
- **§1.2** No PII or sensitive data in log output. Job payloads may contain user data — never log `Message` (payload) contents at Info level or above.
- **§1.3** All database mutations must use transactions or EF Core's implicit `SaveChanges` transaction. Explicit `BeginTransactionAsync` for multi-step mutations (see `JobCommandService.DeleteJob`).
- **§1.4** Row-level locking via `TagWith(InterceptorConstants.RowLockTableJob)` for any fetch-then-update pattern. Never skip the lock tag on competitive fetches.

### §2 — Architecture Patterns

- **§2.1** Everything is a Job. No separate entity tables for messages or batches. Use `Kind` discriminator and `ParentJobId` chain.
- **§2.2** Workers are pure executors. Never add orchestration, routing, or parent/child logic to worker code. That belongs in an `IServerTask` implementation driven by `ServerTaskHost<TContext>`.
- **§2.3** Every background task implements `IServerTask` and is registered as `Scoped`. Wake-up is push-driven via `ServerTaskSignals<TContext>` (`SignalJobFinalized` / `SignalMessageEnqueued`) rather than reducing poll intervals; tasks that don't need push (heartbeat, cleanups) just poll at `DefaultInterval`.
- **§2.4** Services expose interfaces (`IJobCommandService`, `IJobQueryService`, etc.). Generic implementations take `TContext : DbContext`.
- **§2.5** `AddWarp<TContext>()` / `AddWarpWorker<TContext>()` auto-configure the user's DbContext. Users register their DbContext normally — no manual Warp configuration needed.
- **§2.6** In-memory requests (`IRequest<TResponse>`) go through `IMediator.Send()` — same `IPipelineBehavior` pipeline as jobs/messages, but no database persistence.
- **§2.7** In-memory streams (`IStreamRequest<TResponse>`) go through `IMediator.CreateStream()` — `IPipelineBehavior` applies at request level, `IStreamPipelineBehavior` wraps enumeration, returns `IAsyncEnumerable<TResponse>`, no database persistence.
- **§2.8** Future-dated jobs land in `State.Scheduled`; `ScheduledJobActivation` flips them to `Enqueued` when `ScheduleTime <= now`. Activation cadence is controlled by `WarpWorkerConfiguration.ScheduledActivationInterval` (default 5s) — this is the worst-case latency between `ScheduleTime` and pickup eligibility. The task is time-driven and does not participate in DB-push wake-up; push only accelerates what happens *after* activation. Worker fetch queries always check `CurrentState == Enqueued` with a defensive `ScheduleTime <= now` predicate for pre-upgrade legacy rows. Adding new query sites that filter by `Enqueued` without the time predicate is a latent bug on upgraded deployments.
- **§2.9** DB push is an opt-in addon. `opt.UseDatabasePush()` on the builder replaces the default `NullNotificationTransport` with a provider-specific one (Postgres LISTEN/NOTIFY or SQL Server Service Broker) and registers `NotificationListenerTask`. The provider-specific transport is resolved via `IWarpNotificationTransportFactory`, which the provider package (`Warp.Provider.PostgreSql` / `Warp.Provider.SqlServer`) registers when you call `opt.UsePostgreSql()` / `opt.UseSqlServer()` — call the provider first, push second. Worker-fetch push only fires when `UseDispatcher = true`. Transports must not throw from `PublishAsync` — they log + increment `WarpTelemetry.NotificationPublishFailures` instead. Missed notifications are caught by drain-on-reconnect in the listener.
- **§2.10** Realtime dashboard push is an opt-in addon. `opt.AddDashboardPush()` on the builder registers a `WarpDashboardHub` (SignalR) at `${RoutePrefix}/api/hub` plus a `DashboardBroadcaster<TContext>` `BackgroundService` that subscribes to `ServerTaskSignals<TContext>` (the same surface the Orchestrator and MessageRouter consume — making the broadcaster the *third* consumer of that signal pipe) and broadcasts `JobFinalized` / `MessageEnqueued` events to all connected clients. Each broadcast carries the current `DashboardStatistics` DTO as the SignalR payload (one `IDashboardStatsService.GetWarpStatus()` query per coalesce window, fanned to N clients — vs the polling baseline of N × `GET /api/status` per event). Stats fetch is best-effort: on failure the event still fires without a payload and the SPA falls through to its REST refetch path. Per-view data (filtered job lists, job detail, logs) is **not** pushed — those surfaces stay on event-driven REST refetch because the data is per-viewer scoped (filters/pages/jobIds differ per client). The hub URL contains `/api/` so the existing `WarpUIMiddleware` 401-on-API-paths gating covers negotiate and WebSocket upgrades — no parallel auth code path. Multi-server fanout reuses §2.9 (DB push transport carries the signals across servers); without `UseDatabasePush()`, push is single-server only. The broadcaster runs out-of-band of the worker fetch/execute path (§6.1). Frontend probes `GET ${RoutePrefix}/api/dashboard/push/probe` once at boot and falls back to its existing polling path (now at a 30s safety-net interval) when the addon is absent — same hide-on-404 idiom as `/api/concurrency` (§8.6).

### §3 — Coding Style

> Full guidelines: `.claude/references/coding-guidelines.md`

- **§3.1** `var` for all local variables. No explicit types.
- **§3.2** Private fields: `_camelCase`. Public members: `PascalCase`. Interfaces: `IPascalCase`. Constants/static readonly: `PascalCase` (no underscore).
- **§3.3** Braces on all control flow (`if`, `else`, `while`, `for`, `foreach`) — even single-line bodies.
- **§3.4** File-scoped namespaces. One type per file unless handler + request + response grouped together.
- **§3.5** Lambda parameter is `x`; nested lambdas use `y`, `z`.
- **§3.6** Split chained LINQ methods onto separate lines. Place `.` at the start of each line.

```csharp
// good
var activeJobs = await _context.Set<Job>()
    .Where(x => x.CurrentState == State.Enqueued)
    .Where(x => x.ScheduleTime <= now)
    .Select(x =>
        new JobSummary
        {
            Id = x.Id,
            Type = x.Type,
        })
    .ToListAsync();
```

- **§3.7** Separate multiple `&&` conditions into multiple `.Where()` calls.
- **§3.8** Use `.Where()` before `.FirstOrDefault()` / `.SingleOrDefault()` — don't put predicates in the terminal method.
- **§3.9** Blank line before `return` statements. No double blank lines. Private methods last in file.
- **§3.10** Avoid `else` — return early (guard clauses).
- **§3.11** Use object initializers. Omit `()` in `new Type { ... }`.
- **§3.12** No `this.` prefix. No meaningless comments or XML doc on internal code.
- **§3.13** `using` directives outside namespace. `System.*` first, alphabetically sorted.
- **§3.14** Use simple `using var x = ...;` over `using (var x = ...) { }`.
- **§3.15** Prefer `??` over ternary null checks. Prefer ternary over `if/else` for simple assignments.
- **§3.16** Separate type members with a single blank line, except consecutive private fields (no blank line between them).
- **§3.17** Create variables close to where they're used, not at the top of the method.
- **§3.18** No helper lists — use `Select` or `yield return` instead.
- **§3.19** Place `new` keyword on a new line in `Select` projections, indented one level deeper than `Select`.
- **§3.20** Use `string.Equals` with `StringComparison` instead of `==` for string comparison (enforced by MA0006).

### §4 — Testing Standards

- **§4.1** Every new/changed public method must have tests. Both PostgreSQL and SQL Server subclasses.
- **§4.2** Unit test pattern: abstract base class with `IDatabaseFixture`, concrete subclasses per database with `[Collection("PostgreSql")]` / `[Collection("SqlServer")]`.
- **§4.3** Each unit test calls exactly ONE public method on ONE class. State set up via direct DB inserts.
- **§4.4** Fresh `CreateContext()` for setup, act, and assert — no shared tracking.
- **§4.5** Integration tests use `WarpTestServer` with `Server.WaitForCompletion()` / `Server.WaitForJobState()`. Use `[Collection("PostgreSql-Integration")]` / `[Collection("SqlServer-Integration")]` fixtures.
- **§4.6** No `Task.Delay` in tests except handlers designed to be cancelled.
- **§4.7** Test handlers are simple: empty body, throw, or increment counter. No unnecessary abstractions.
- **§4.8** Test naming: `MethodName_Scenario_ExpectedResult`.
- **§4.9** Use Shouldly for assertions (`job.CurrentState.ShouldBe(State.Completed)`).
- **§4.10** Cancel long-running jobs at end of integration tests to avoid blocking.

### §5 — Data Layer

- **§5.1** No raw SQL. All queries use EF Core LINQ. This ensures dual-database compatibility. **Exception**: provider-native APIs with no EF Core abstraction are allowed inside the provider packages (`src/core/providers/Warp.Provider.PostgreSql/` — LISTEN/NOTIFY via `NpgsqlConnection`, row-lock SQL; `src/core/providers/Warp.Provider.SqlServer/` — Service Broker via `SqlConnection`, row-lock SQL). `Warp.Core` itself must stay provider-agnostic — no `Npgsql` or `Microsoft.Data.SqlClient` references.
- **§5.2** No `_context.Set<>()` subqueries inside `.Select()` projections. Use navigation properties or two-step fetch.
- **§5.3** `AsNoTracking()` on read-only queries. `Select()` projections over `Include()` for reads.
- **§5.4** EF Core entity configurations applied via `WarpModelCustomizer` (auto-registered by `AddWarp`). Fluent API in `OnModelCreating` overrides.
- **§5.5** DbContext lifetime is `Scoped`. Never register as Transient — outbox pattern requires shared instance.
- **§5.6** `TimeProvider` for all timestamps. Never `DateTime.UtcNow` in production code.
- **§5.7** One `SaveChanges` per handler/operation. Services should not call `SaveChanges` — the caller saves.
- **§5.8** Add entities to context just before `SaveChanges`, not at the top of the method.
- **§5.9** Use async EF Core methods (`ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`).

### §6 — Performance

- **§6.1** Worker hot path is sacred. No additional logic in fetch/execute cycle.
- **§6.2** Statistics use Counter rows (write-optimized) aggregated by `CounterAggregator`. Never update Statistic rows directly.
- **§6.3** Signal-driven wakeup for background tasks. Avoid reducing poll intervals as a performance hack.
- **§6.4** Select only needed columns from database — never load full entities for read-only display.
- **§6.5** Avoid initializing collections inside loops.

### §7 — Git & Workflow

- **§7.1** Branch naming: hierarchical with `/` — e.g., `feat/multi-server-tests`, `fix/calculator-multiplication`.
- **§7.2** Commit messages: imperative mood, describe the "what" concisely — e.g., "Add deterministic ordering to server/worker API queries".
- **§7.3** Static analyzers enforced in build: StyleCop, Roslynator, SonarAnalyzer, Meziantou. All in `Directory.Build.props`. Build must pass with zero warnings.
- **§7.4** `.editorconfig` enforces code style at the IDE level. Do not override severity levels in individual projects.

### §8 — Project-Specific Patterns

- **§8.1** `WarpConfiguration` via `IOptions<WarpConfiguration>`. All configurable values go through this pattern.
- **§8.2** Failed jobs never auto-deleted (`ExpireAt = null`). Only explicit user action or count-based cleanup.
- **§8.3** `ContinuationOptions` is generalized to all job kinds — any job with children can control child activation on failure.
- **§8.4** `RequeueJob` resets `ScheduleTime` to now. Requeued jobs always execute immediately.
- **§8.5** Cancellation uses `CancellationMode` enum, not immediate state change. Worker monitors and cancels handler token.
- **§8.6** Concurrency control (Mutex + Semaphore) is an opt-in addon (`opt.AddConcurrency()` on the builder) in `Warp.Core.Concurrency`. `ConcurrencyPipelineBehavior` uses `IWarpSemaphoreProvider` (Postgres: N-distinct-named-locks trick over `IDistributedLockProvider`; SQL Server: Medallion's `SqlDistributedSemaphore`). At `limit = 1` the call passes through to a single named lock — byte-identical to a distributed mutex. Set key via `.WithMutex("key")` / `[Mutex("key")]` (limit = 1) or `.WithSemaphore("key", N)` / `[Semaphore("key", N)]` (limit > 1). Key + limit + mode stored in `IConcurrencyMetadata`. Two policies via `ConcurrencyMode`: `Skip` (`[Mutex]` default — surplus short-circuited to `Deleted`) and `Wait` (`[Semaphore]` default — surplus requeued with `ScheduleTime = now`, audit log shows `Requeued`). Order across requeued jobs is best-effort; not strict FIFO. Disjoint namespaces by design: `[Mutex("k")]` uses lock `warp:concurrency:k`; `[Semaphore("k", N)]` uses one of `warp:concurrency:k:0..k:{N-1}`. Mixing both attributes against the same key produces independent limits, not a unified one. Admin overrides via `IConcurrencyLimitManager` (scoped service `ConcurrencyLimitResolver`, precedence `admin row > meta.Limit > 1`); runtime-editable from `/warp/concurrency`. Hide-on-404: dashboard probes `/api/concurrency` and hides the nav entry if `AddConcurrency` is not registered.
- **§8.7** `RecurringJobScheduler` creates jobs, `AddOrUpdateRecurringJob` only registers/updates definitions.
- **§8.8** Source generator (`Warp.SourceGenerator`) for zero-allocation mediator and worker dispatch.
- **§8.9** Rate limiting (Fixed + Sliding windows) is an opt-in addon (`opt.AddRateLimit()` on the builder) in `Warp.Core.RateLimit`. Unlike Mutex/Semaphore which hold the lock for the duration of the handler, `RateLimitPipelineBehavior` holds `IWarpLockProvider` only for the check-and-increment, then releases — window counts must persist across sessions (unlike ephemeral advisory locks), so the live state lives in the `RateLimitBucket` entity (`Name` PK, `WindowStartUtc`, `CurrentCount`, `TimestampsJson` for Sliding, `UpdatedAt`). Two attribute styles: `Fixed` (default, floor-aligned to global UTC tick boundaries) and `Sliding` (rolling window over last N starts as JSON-encoded ticks, defensively trimmed to `count` on read so corrupted state self-heals). Two policies via `RateLimitMode`: `Skip` (default — surplus short-circuited to `Deleted` with `Cancelled — rate limit '{key}' exceeded ({count}/{window}s)` log) and `Wait` (surplus rescheduled to `nextAvailable` via `JobOutcome.RescheduledState`, audit log `Throttled — rate limit '{key}' rescheduled to <iso>`). Apply via `[RateLimit("k", count, perSeconds, Mode = ..., Style = ...)]` (class-level on `IJob`) or `parameters.WithRateLimit("k", count, window, mode, style)`. `perSeconds` capped at `RateLimitAttribute.MaxWindowSeconds` (7 days); admin manager enforces the same cap. Lock-timeout requeues add 100–500ms randomised jitter to avoid thundering-herd. Key + count + window + mode + style stored in `IRateLimitMetadata` (properties are `RateLimitKey` / `RateLimitCount` / `RateLimitWindowSeconds` / `RateLimitMode` / `RateLimitStyle` — every metadata property in the codebase is now addon-prefixed; `IConcurrencyMetadata` carries `ConcurrencyKey` / `ConcurrencyLimit` / `ConcurrencyMode` after the same sweep — so jobs can carry any combination of metadata interfaces without dict-key collision). Admin overrides via `IRateLimitManager` (scoped, `RateLimitResolver` caches lookups per scope, precedence `admin row > attribute/metadata`); runtime-editable from `/warp/ratelimits`, persisted in `RateLimitOverride(Name, Count, WindowSeconds, UpdatedAt)`. Hot-path upsert uses `RateLimitStore<TContext>` with `ExecuteUpdateAsync` first then fresh-scope insert on miss — same pattern as `CircuitBreakerStore`, and one of the few §5.7 deviations (pipeline must commit before yielding to the handler). Composition order matters when both `[Mutex]` and `[RateLimit]` apply: register `AddConcurrency()` before `AddRateLimit()` so mutex rejections don't waste rate-limit tokens. Emits `warp.rate_limit_check` OTel span with `warp.rate_limit.{key,count,window_seconds,style,outcome}` tags (outcome ∈ {`acquired`, `skipped`, `throttled`, `lock_contention`}). Endpoints validate name length at the boundary (≤ 200 chars) to return 400 rather than letting `ArgumentException` bubble as 500. Keys appear in logs/dashboard — keep PII out of the key value. DB-push doesn't accelerate Wait-mode reschedules (they land in `State.Scheduled` and depend on `ScheduledJobActivation` polling). Hide-on-404: dashboard probes `/api/ratelimits` and hides the nav entry if `AddRateLimit` is not registered.
