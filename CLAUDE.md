# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Jobly

Jobly is a distributed job processing and message queue library for .NET 10. Four patterns:

- **Messages** (`IMessage`) — Pub/sub queue. Multiple handlers per message, each an independent job.
- **Jobs** (`IJob`) — Orchestrated background work. Single handler, scheduling, retries, continuations, batches, named queues.
- **Requests** (`IRequest<TResponse>`) — In-memory request/response. Single handler, no database persistence, returns typed response immediately via `IMediator.Send()`.
- **Streams** (`IStreamRequest<TResponse>`) — In-memory streaming. Single handler, no database persistence, returns `IAsyncEnumerable<TResponse>` via `IMediator.CreateStream()`.

**Optional DB push**: `opt.UseDatabasePush()` on the builder replaces polling wake-up with push notifications (Postgres LISTEN/NOTIFY, SQL Server Service Broker) for the dispatcher, `MessageRoutingTask`, and `OrchestrationTask`. Worker fetch push requires `UseDispatcher = true` — individual-worker mode has a thundering-herd problem and is left on polling. See §2.9 and the DB Push section in `README.md`.

Ships as NuGet packages: `Jobly.Core` (provider-agnostic), `Jobly.PostgreSql` (PG-specific), `Jobly.SqlServer` (SQL Server-specific), `Jobly.Worker` (worker runtime), `Jobly.UI` (dashboard). Users install the provider package that matches their database and call `opt.UsePostgreSql()` or `opt.UseSqlServer()` inside the `AddJobly` / `AddJoblyWorker` lambda.

## Build & Test Commands

```bash
# Backend (from src/)
dotnet build Jobly.slnx
dotnet test --project core/Jobly.Tests/Jobly.Tests.csproj                                          # Full suite (947 tests, ~2m 40s)

# By database requirement (CI uses this matrix)
dotnet test --project core/Jobly.Tests/Jobly.Tests.csproj -- --filter-trait "Category=NoDb"        # Pure-unit, no container (~6s)
dotnet test --project core/Jobly.Tests/Jobly.Tests.csproj -- --filter-trait "Category=PostgreSql"  # PG-backed (~1m)
dotnet test --project core/Jobly.Tests/Jobly.Tests.csproj -- --filter-trait "Category=SqlServer"   # SQL Server-backed (~1m 40s)

# Run specific test suites
dotnet test --project core/Jobly.Tests/Jobly.Tests.csproj -- --filter-namespace "Jobly.Tests.Unit"         # Unit tests only
dotnet test --project core/Jobly.Tests/Jobly.Tests.csproj -- --filter-namespace "Jobly.Tests.Integration"  # Integration tests only

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

Workers are **pure executors** — they only fetch and execute `Kind=Job` jobs. All orchestration is handled by dedicated background tasks (all extend `ServerTaskBase`):

```
Worker (pure executor):
  1. Fetch Job (Kind=Job, Enqueued, ScheduleTime < now)
  2. Execute handler (pipeline behaviors run first — mutex, retry, etc.)
  3. Update state, counters, logs
  4. Signal orchestrator

MessageRoutingTask (polls every 1s):
  - Routes Kind=Message jobs → discovers handlers, creates N child jobs

OrchestrationTask (signal + 10s sweep):
  - Finalizes parents when all children reach terminal state
  - Activates continuations (Awaiting children of completed parents)
```

### Job Cancellation

Uses `CancellationMode` enum (`None=0, Graceful=1`) instead of immediate state change. When a processing job is cancelled:
1. `DeleteJob` sets `CancellationMode = Graceful` (state stays `Processing`, no `ExpireAt` yet)
2. `RunJobMonitor` detects `CancellationMode != None` and cancels the handler's `CancellationToken`
3. If handler respects the token: worker sets state to `Deleted`, `ExpireAt`, clears `CancellationMode`
4. If handler ignores the token and completes: state is `Completed` (work happened), `CancellationMode` cleared
5. Job stays in Processing tab with "Cancelling..." badge until handler actually exits

### Mutex (Concurrency Control)

Opt-in addon via `services.AddJoblyMutex()`. Only one job per concurrency key can be Processing at a time. Set at publish time via `.WithMutex("key")` extension or `[Mutex("key")]` attribute on the job class. Implemented as `MutexPipelineBehavior` — acquires a distributed lock before the handler runs, short-circuits to Deleted via `IJobContext.Outcome` if the lock is held. Lock released after handler completes. Zero overhead for jobs without a key. Concurrency key stored in job metadata (not a dedicated column).

### Recurring Jobs

- `AddOrUpdateRecurringJob` only registers/updates the definition (cron, message, type). **Does not create jobs.** Acquires a distributed lock on the recurring job name, saves immediately (exception to §5.7 — lock must be held during save to prevent race conditions).
- `RecurringJobSchedulerTask` creates jobs with `ScheduleTime = now` (ready for immediate execution) and sets `NextExecution` to the next cron occurrence.
- **RecurringJobLog** — Immutable audit trail linking recurring jobs to their created jobs. Fields: `Id, RecurringJobId, JobId (nullable), CreatedAt`. `JobId` has FK with `SET NULL` cascade — when the job is cleaned up, `JobId` becomes null but the log entry survives. Navigation property `Job` for clean LINQ queries.
- Scheduler uses RecurringJobLog for dedup: checks if the most recent log entry's job is still Enqueued/Processing via nav property.
- ExpirationCleanupTask retains last 100 logs per recurring job (uses `HAVING COUNT > 100` to skip most).

### Dashboard Authorization

Two modes:
- **Built-in login**: `options.UseBuiltInLogin<TValidator>()` — Jobly serves a React login page, manages HTTP-only signed cookie (7 day expiry via ASP.NET Data Protection). Register `IJoblyCredentialValidator` in DI (scoped, can inject DbContext). Login/logout via `/api/auth/login` and `/api/auth/logout`. SPA catches 401 from Axios interceptor and shows login component.
- **Custom redirect**: `options.Authorization = new MyFilter()` + `options.UnauthorizedRedirectUrl = "/login"` — your app handles login. Filter checks `HttpContext` (claims, roles, etc.). API gets 401, SPA gets 302 redirect.

Default: no auth (open access).

### Key Design Decisions

- **No raw SQL** — all queries use EF Core LINQ. No `_context.Set<>()` subqueries inside `.Select()` projections — use navigation properties or two-step fetch instead.
- **Naming conventions respected** — entity configurations do NOT use `.ToTable()`, so EF Core naming conventions (e.g., `UseSnakeCaseNamingConvention()`) can transform table/column names freely. Schema is set via `entity.Metadata.SetSchema()` to avoid re-pinning table names.
- **Default schema** — All Jobly tables default to the `"jobly"` schema. Configurable via `JoblyConfiguration.Schema`. Set to `null` for the database's default schema.
- **TimeProvider** — all production code uses injectable `TimeProvider` instead of `DateTime.UtcNow`. Registered as `TryAddSingleton(TimeProvider.System)` in `AddJobly`. Test code can use `DateTime.UtcNow` directly.
- **DbContext must be registered as Scoped** (not Transient). The outbox pattern requires the publisher and application code to share the same DbContext instance within a scope.
- Everything is a Job. `ParentJobId` replaces both the old MessageId and BatchId foreign keys.
- Workers never touch parent/child orchestration — that's the OrchestrationTask's job.
- `ContinuationOptions` is generalized: any job with children can control whether children activate on failure.
- Failed jobs never auto-deleted.
- Statistics use Counter rows (write-optimized) aggregated into Statistic rows by CounterAggregatorTask.
- All background tasks extend `ServerTaskBase` with signal support (semaphore-based wake-up, capped at 1).
- `RequeueJob` resets `ScheduleTime` to now — requeued jobs always execute immediately.
- Count-based cleanup: `MaxExpirableJobCount` (default null/disabled) — deletes oldest by `ExpireAt` when threshold exceeded. Failed jobs excluded (null `ExpireAt`).
- `JobExpirationTimeout` configurable on base `JoblyConfiguration` (default 1 day). Used by worker, command service, and orchestration task.
- Job metadata: `JobParameters.Metadata` (key-value dictionary), stored as JSON on the Job entity. `IJobContext` gives handlers access to metadata, job ID, and trace ID. `IPublishPipelineBehavior<T>` intercepts publish for cross-cutting metadata. Metadata inherited by child jobs via ambient context.
- Pause/Resume: Server and worker group level. `PausedAt` timestamp on Server and WorkerGroup entities. `PauseStateHolder` (in-memory snapshot) updated by `HeartbeatTask`, checked by workers before each poll. API endpoints: `POST /api/servers/{id}/pause|resume`, `POST /api/groups/{id}/pause|resume`.
- Real-time log flushing: `RunJobMonitor` drains `JobLogCollector` every ~1s during handler execution and persists logs to the database. Logs visible in dashboard while the job is still processing.

### Backend (.NET 10)

- **Jobly.Core** — Entities (Job, RecurringJob, RecurringJobLog, JobLog, Server, Worker, ServerTask, ServerLog), handlers, JobDispatcher (cached reflection), Publisher, BatchPublisher, logging (JobLogContext/JobLoggerProvider). Services: `JobQueryService`, `JobCommandService`, `JobGroupQueryService`, `RecurringJobService`, `DashboardStatsService`.
- **Jobly.Worker** — JoblyWorkerService (pure executor), JoblyDispatcher/JoblyDispatcherWorker (batch-fetch mode), worker groups. Background tasks (all extend ServerTaskBase): HeartbeatTask, CounterAggregatorTask, ServerCleanupTask, StaleJobRecoveryTask, ExpirationCleanupTask, RecurringJobSchedulerTask, MessageRoutingTask, OrchestrationTask.
- **Jobly.UI** — Minimal API endpoints + embedded SPA served at `/jobly`. Auth middleware (`IJoblyAuthorizationFilter`, `IJoblyCredentialValidator`, built-in cookie login). Typed `config.ts` for window globals.
- **Static analyzers** — StyleCop, Roslynator, SonarAnalyzer, Meziantou.

### Frontend (Vite + React 18 + TypeScript)

`src/ui/`. Tailwind + shadcn/ui, Zustand, Axios (with 401 interceptor). Dashboard with clickable metric cards, realtime + historical graphs, dark mode. Job list by state with bulk actions. Failed jobs type filter with bulk delete/requeue by type. Job detail with colored state cards, handler output, "Cancelling..." badge, mutex key. Batch progress bar (stacked green/red). Recurring job execution history with "Cleaned up" for deleted jobs. Worker detail page with activity log. Built-in login page component. Logout button in navbar (when built-in login active).

## Testing

632 tests (316 PostgreSQL + 316 SQL Server) using xUnit, Shouldly, Testcontainers + Respawn (~30s).

### Test Structure

Three categories:

**Unit tests** (`src/core/Jobly.Tests/Unit/`) — ~500 tests, ~15s:
- Each test calls exactly ONE public method on ONE class
- State set up via direct DB inserts, not via other services
- Fresh `CreateContext()` for setup, act, and assert (no shared tracking)
- Use `[Collection("PostgreSql")]` / `[Collection("SqlServer")]` fixtures (no server running)

**Integration tests** (`src/core/Jobly.Tests/Integration/`) — ~72 tests, ~35s:
- Use `JoblyTestServer` — boots full worker + all background tasks against a real database
- Tests publish jobs, wait for results via `Server.WaitForCompletion()` / `Server.WaitForJobState()`
- Use `[Collection("PostgreSql-Integration")]` / `[Collection("SqlServer-Integration")]` fixtures (server running)
- Server shared per collection fixture (boots once, Respawn between tests with retry on lock contention)

**Multi-server integration tests** (`src/core/Jobly.Tests/Integration/MultiServerTests.cs`) — 16 tests, ~5s:
- Use 2 `JoblyTestServer` instances (3 workers each) sharing one database container
- Verify distributed coordination: row locks, distributed advisory locks, orchestration, message routing, mutex
- Use `[Collection("PostgreSql-MultiServer")]` / `[Collection("SqlServer-MultiServer")]` fixtures
- `IMultiServerDatabaseFixture` provides `Server1` and `Server2`

**Dashboard auth tests** (`DashboardAuthTests`) — 7 tests, ~400ms:
- Uses `WebApplication` with `TestServer` — no database, no Jobly services
- Tests auth middleware in isolation: login, logout, cookie flow, filter, redirect

### Writing Unit Tests

```csharp
public abstract class MyTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    protected MyTestsBase(IDatabaseFixture fixture) => _fixture = fixture;
    public async Task InitializeAsync() => await _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MethodName_Scenario_ExpectedResult()
    {
        // Arrange: insert state directly into DB
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job { Id = jobId, Kind = JobKind.Job, CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow, ScheduleTime = DateTime.UtcNow, Queue = "default" });
        await ctx.SaveChangesAsync();

        // Act: call ONE method on ONE class
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));
        await svc.DeleteJob(jobId);

        // Assert: query DB for result
        var job = await _fixture.CreateContext().Set<Job>().FindAsync(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }
}

// Concrete subclasses for dual-database
[Collection("PostgreSql")]
public class MyTests_PostgreSql : MyTestsBase
{
    public MyTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class MyTests_SqlServer : MyTestsBase
{
    public MyTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
```

### Writing Integration Tests

```csharp
public abstract class MyIntegrationTestsBase : IntegrationTestBase
{
    protected MyIntegrationTestsBase(IDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GivenWorkload_WhenProcessed_ThenExpectedResult()
    {
        var publisher = Server.CreatePublisher();
        await publisher.Enqueue(new MyRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }
}

[Collection("PostgreSql-Integration")]
public class MyIntegrationTests_PostgreSql : MyIntegrationTestsBase
{
    public MyIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture) : base(fixture) { }
}

[Collection("SqlServer-Integration")]
[Trait("Category", "SqlServer")]
public class MyIntegrationTests_SqlServer : MyIntegrationTestsBase
{
    public MyIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture) : base(fixture) { }
}
```

### Test Principles

- Each test tests ONE public method. No chaining multiple calls to simulate flows.
- If testing a multi-step flow (worker + orchestration + routing), use integration tests with `JoblyTestServer`.
- No `Task.Delay` in tests except for handlers meant to be cancelled (`CancellableCommand`).
- No unnecessary abstractions — test handlers are simple (empty, throw, increment counter).
- Tests run on both PostgreSQL and SQL Server via abstract base + concrete subclasses.
- Cancel long-running jobs at end of integration tests so they don't block (~600ms vs 30s).

### Registration

`AddJobly<TContext>(opt => ...)` / `AddJoblyWorker<TContext>(opt => ...)` take a single `Action<JoblyBuilder<TContext>>` / `Action<JoblyWorkerBuilder<TContext>>` lambda and automatically configure the user's DbContext:
- Wraps the existing `DbContextOptions<TContext>` service descriptor to add row-lock interceptors
- Replaces `IModelCustomizer` with `JoblyModelCustomizer` to auto-apply entity configurations
- Registers `TimeProvider.System` via `TryAddSingleton` (overridable for testing)
- Registers `IJoblyLockProvider` (wraps `IDistributedLockProvider` — Medallion.Threading is internal; the concrete `IDistributedLockProvider` is registered by the provider package)
- Users just register their DbContext normally — no manual configuration needed
- The builder inherits from `JoblyConfiguration` / `JoblyWorkerConfiguration`, so config fields (`WorkerCount`, `PollingInterval`, `DefaultQueue`, etc.) are set directly on `opt`
- Opt-in addons on the builder: `opt.AddRetry()`, `opt.AddMutex()`, `opt.AddCircuitBreaker()`, `opt.AddNoRestart()`, `opt.UseDatabasePush()`
- Provider selection via `opt.UsePostgreSql()` / `opt.UseSqlServer()` (from `Jobly.PostgreSql` / `Jobly.SqlServer`) — mandatory for real use; registers `IJoblySqlQueries`, `IDatabaseExceptionClassifier`, `IJoblyNotificationTransportFactory`, and the provider-specific `IDistributedLockProvider`

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
- **§2.2** Workers are pure executors. Never add orchestration, routing, or parent/child logic to worker code. That belongs in `ServerTaskBase` subclasses.
- **§2.3** All background tasks extend `ServerTaskBase` with signal support. Use `Signal()` to wake tasks instead of reducing poll intervals.
- **§2.4** Services expose interfaces (`IJobCommandService`, `IJobQueryService`, etc.). Generic implementations take `TContext : DbContext`.
- **§2.5** `AddJobly<TContext>()` / `AddJoblyWorker<TContext>()` auto-configure the user's DbContext. Users register their DbContext normally — no manual Jobly configuration needed.
- **§2.6** In-memory requests (`IRequest<TResponse>`) go through `IMediator.Send()` — same `IPipelineBehavior` pipeline as jobs/messages, but no database persistence.
- **§2.7** In-memory streams (`IStreamRequest<TResponse>`) go through `IMediator.CreateStream()` — `IPipelineBehavior` applies at request level, `IStreamPipelineBehavior` wraps enumeration, returns `IAsyncEnumerable<TResponse>`, no database persistence.
- **§2.8** Future-dated jobs land in `State.Scheduled`; `ScheduledJobActivationTask` flips them to `Enqueued` when `ScheduleTime <= now`. Activation cadence is controlled by `JoblyWorkerConfiguration.ScheduledActivationInterval` (default 5s) — this is the worst-case latency between `ScheduleTime` and pickup eligibility. The task is time-driven and does not participate in DB-push wake-up; push only accelerates what happens *after* activation. Worker fetch queries always check `CurrentState == Enqueued` with a defensive `ScheduleTime <= now` predicate for pre-upgrade legacy rows. Adding new query sites that filter by `Enqueued` without the time predicate is a latent bug on upgraded deployments.
- **§2.9** DB push is an opt-in addon. `opt.UseDatabasePush()` on the builder replaces the default `NullNotificationTransport` with a provider-specific one (Postgres LISTEN/NOTIFY or SQL Server Service Broker) and registers `NotificationListenerTask`. The provider-specific transport is resolved via `IJoblyNotificationTransportFactory`, which the provider package (`Jobly.PostgreSql` / `Jobly.SqlServer`) registers when you call `opt.UsePostgreSql()` / `opt.UseSqlServer()` — call the provider first, push second. Worker-fetch push only fires when `UseDispatcher = true`. Transports must not throw from `PublishAsync` — they log + increment `JoblyTelemetry.NotificationPublishFailures` instead. Missed notifications are caught by drain-on-reconnect in the listener.

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
- **§4.5** Integration tests use `JoblyTestServer` with `Server.WaitForCompletion()` / `Server.WaitForJobState()`. Use `[Collection("PostgreSql-Integration")]` / `[Collection("SqlServer-Integration")]` fixtures.
- **§4.6** No `Task.Delay` in tests except handlers designed to be cancelled.
- **§4.7** Test handlers are simple: empty body, throw, or increment counter. No unnecessary abstractions.
- **§4.8** Test naming: `MethodName_Scenario_ExpectedResult`.
- **§4.9** Use Shouldly for assertions (`job.CurrentState.ShouldBe(State.Completed)`).
- **§4.10** Cancel long-running jobs at end of integration tests to avoid blocking.

### §5 — Data Layer

- **§5.1** No raw SQL. All queries use EF Core LINQ. This ensures dual-database compatibility. **Exception**: provider-native APIs with no EF Core abstraction are allowed inside the provider packages (`src/core/Jobly.PostgreSql/` — LISTEN/NOTIFY via `NpgsqlConnection`, row-lock SQL; `src/core/Jobly.SqlServer/` — Service Broker via `SqlConnection`, row-lock SQL). `Jobly.Core` itself must stay provider-agnostic — no `Npgsql` or `Microsoft.Data.SqlClient` references.
- **§5.2** No `_context.Set<>()` subqueries inside `.Select()` projections. Use navigation properties or two-step fetch.
- **§5.3** `AsNoTracking()` on read-only queries. `Select()` projections over `Include()` for reads.
- **§5.4** EF Core entity configurations applied via `JoblyModelCustomizer` (auto-registered by `AddJobly`). Fluent API in `OnModelCreating` overrides.
- **§5.5** DbContext lifetime is `Scoped`. Never register as Transient — outbox pattern requires shared instance.
- **§5.6** `TimeProvider` for all timestamps. Never `DateTime.UtcNow` in production code.
- **§5.7** One `SaveChanges` per handler/operation. Services should not call `SaveChanges` — the caller saves.
- **§5.8** Add entities to context just before `SaveChanges`, not at the top of the method.
- **§5.9** Use async EF Core methods (`ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`).

### §6 — Performance

- **§6.1** Worker hot path is sacred. No additional logic in fetch/execute cycle.
- **§6.2** Statistics use Counter rows (write-optimized) aggregated by `CounterAggregatorTask`. Never update Statistic rows directly.
- **§6.3** Signal-driven wakeup for background tasks. Avoid reducing poll intervals as a performance hack.
- **§6.4** Select only needed columns from database — never load full entities for read-only display.
- **§6.5** Avoid initializing collections inside loops.

### §7 — Git & Workflow

- **§7.1** Branch naming: hierarchical with `/` — e.g., `feat/multi-server-tests`, `fix/calculator-multiplication`.
- **§7.2** Commit messages: imperative mood, describe the "what" concisely — e.g., "Add deterministic ordering to server/worker API queries".
- **§7.3** Static analyzers enforced in build: StyleCop, Roslynator, SonarAnalyzer, Meziantou. All in `Directory.Build.props`. Build must pass with zero warnings.
- **§7.4** `.editorconfig` enforces code style at the IDE level. Do not override severity levels in individual projects.

### §8 — Project-Specific Patterns

- **§8.1** `JoblyConfiguration` via `IOptions<JoblyConfiguration>`. All configurable values go through this pattern.
- **§8.2** Failed jobs never auto-deleted (`ExpireAt = null`). Only explicit user action or count-based cleanup.
- **§8.3** `ContinuationOptions` is generalized to all job kinds — any job with children can control child activation on failure.
- **§8.4** `RequeueJob` resets `ScheduleTime` to now. Requeued jobs always execute immediately.
- **§8.5** Cancellation uses `CancellationMode` enum, not immediate state change. Worker monitors and cancels handler token.
- **§8.6** Mutex is an opt-in addon (`opt.AddMutex()` on the builder). `MutexPipelineBehavior` uses distributed lock via `IJoblyLockProvider`. Set key via `.WithMutex("key")` or `[Mutex("key")]` attribute. Concurrency key stored in metadata.
- **§8.7** `RecurringJobSchedulerTask` creates jobs, `AddOrUpdateRecurringJob` only registers/updates definitions.
- **§8.8** Source generator (`Jobly.SourceGenerator`) for zero-allocation mediator and worker dispatch.
