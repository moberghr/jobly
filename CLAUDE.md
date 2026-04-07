# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Jobly

Jobly is a distributed job processing and message queue library for .NET 10. Three patterns:

- **Messages** (`IMessage`) — Pub/sub queue. Multiple handlers per message, each an independent job.
- **Jobs** (`IJob`) — Orchestrated background work. Single handler, scheduling, retries, continuations, batches, named queues.
- **Requests** (`IRequest<TResponse>`) — In-memory request/response. Single handler, no database persistence, returns typed response immediately via `IMediator.Send()`.

Ships as NuGet packages (Jobly.Core, Jobly.UI, Jobly.Worker). Supports PostgreSQL and SQL Server.

## Build & Test Commands

```bash
# Backend (from src/)
dotnet build Jobly.sln
dotnet test Jobly.sln --filter "Category!=SqlServer"   # PostgreSQL only
dotnet test Jobly.sln                                    # All databases (464 tests, ~30s)

# Run specific test suites
dotnet test Jobly.sln --filter "FullyQualifiedName~Jobly.Tests.Unit"         # Unit tests only
dotnet test Jobly.sln --filter "FullyQualifiedName~Jobly.Tests.Integration"  # Integration tests only

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
```

### Handler Interfaces

```csharp
public interface IMessageHandler<in T> where T : IMessage { Task HandleAsync(T message, CancellationToken ct); }
public interface IJobHandler<in T> where T : IJob { Task HandleAsync(T message, CancellationToken ct); }
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { Task<TResponse> HandleAsync(TRequest request, CancellationToken ct); }
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse> { Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct); }
```

### Type Hierarchy

All types implement `IRequest<TResponse>`. `IJob` and `IMessage` return `Unit` (void). `IRequest<TResponse>` returns a custom type.

```csharp
public interface IRequest<out TResponse>;              // Base — all types implement this
public interface IJob : IRequest<Unit>;                 // Persistent, single handler
public interface IMessage : IRequest<Unit>;             // Persistent, multiple handlers
// IRequest<TResponse> used directly for in-memory     // In-memory, returns TResponse
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

### Job Entity

Single table with `Kind` enum. Key fields:
- **Shared**: Id, Kind, Type, Message (payload), CreateTime, CurrentState, Queue, ExpireAt, ParentJobId, TraceId, SpawnedByJobId, CancellationMode, ConcurrencyKey
- **Job-specific**: ScheduleTime, HandlerType, MaxRetries, RetriedTimes, CurrentWorkerId, LastKeepAlive
- **Batch-specific**: ContinuationOptions (generalized to all kinds — controls child activation on parent failure)
- **JobLog** — Unified audit trail. EventType: Created, Processing, Completed, Failed, Requeued, Deleted, Cancelled, CancellationRequested, Log (ILogger output). Includes optional `WorkerId` (set by worker-produced entries, null for command/orchestration entries).

### Worker Architecture

Workers are **pure executors** — they only fetch and execute `Kind=Job` jobs. All orchestration is handled by dedicated background tasks (all extend `ServerTaskBase`):

```
Worker (pure executor):
  1. Fetch Job (Kind=Job, Enqueued, ScheduleTime < now)
  2. Mutex check (if ConcurrencyKey set, cancel if another job with same key is Processing)
  3. Execute handler
  4. Update state, counters, logs
  5. Signal orchestrator

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

`ConcurrencyKey` on Job — only one job per key can be Processing at a time. Set at publish time via `JobParameters.Mutex`. Worker checks after marking job Processing — if another job with same key is already Processing, cancels this one with "mutex held" log. Zero overhead for jobs without a key. Same column designed for future semaphore/rate-limiting extension.

### Recurring Jobs

- `AddOrUpdateRecurringJob` only registers/updates the definition (cron, message, type). **Does not create jobs.**
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

### Backend (.NET 10)

- **Jobly.Core** — Entities (Job, RecurringJob, RecurringJobLog, JobLog, Server, Worker, ServerTask, ServerLog), handlers, JobDispatcher (cached reflection), Publisher, BatchPublisher, logging (JobLogContext/JobLoggerProvider). Services: `JobQueryService`, `JobCommandService`, `JobGroupQueryService`, `RecurringJobService`, `DashboardStatsService`.
- **Jobly.Worker** — JoblyWorkerService (pure executor), JoblyDispatcher/JoblyDispatcherWorker (batch-fetch mode), worker groups. Background tasks (all extend ServerTaskBase): HeartbeatTask, CounterAggregatorTask, ServerCleanupTask, StaleJobRecoveryTask, ExpirationCleanupTask, RecurringJobSchedulerTask, MessageRoutingTask, OrchestrationTask.
- **Jobly.UI** — Minimal API endpoints + embedded SPA served at `/jobly`. Auth middleware (`IJoblyAuthorizationFilter`, `IJoblyCredentialValidator`, built-in cookie login). Typed `config.ts` for window globals.
- **Static analyzers** — StyleCop, Roslynator, SonarAnalyzer, Meziantou.

### Frontend (Vite + React 18 + TypeScript)

`src/ui/`. Tailwind + shadcn/ui, Zustand, Axios (with 401 interceptor). Dashboard with clickable metric cards, realtime + historical graphs, dark mode. Job list by state with bulk actions. Failed jobs type filter with bulk delete/requeue by type. Job detail with colored state cards, handler output, "Cancelling..." badge, mutex key. Batch progress bar (stacked green/red). Recurring job execution history with "Cleaned up" for deleted jobs. Worker detail page with activity log. Built-in login page component. Logout button in navbar (when built-in login active).

## Testing

464 tests (232 PostgreSQL + 232 SQL Server) using xUnit, Shouldly, Testcontainers + Respawn (~30s).

### Test Structure

Three categories:

**Unit tests** (`src/core/Jobly.Tests/Unit/`) — ~400 tests, ~14s:
- Each test calls exactly ONE public method on ONE class
- State set up via direct DB inserts, not via other services
- Fresh `CreateContext()` for setup, act, and assert (no shared tracking)
- Use `[Collection("PostgreSql")]` / `[Collection("SqlServer")]` fixtures (no server running)

**Integration tests** (`src/core/Jobly.Tests/Integration/`) — ~56 tests, ~16s:
- Use `JoblyTestServer` — boots full worker + all background tasks against a real database
- Tests publish jobs, wait for results via `Server.WaitForCompletion()` / `Server.WaitForJobState()`
- Use `[Collection("PostgreSql-Integration")]` / `[Collection("SqlServer-Integration")]` fixtures (server running)
- Server shared per collection fixture (boots once, Respawn between tests with retry on lock contention)

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

`AddJobly<TContext>()` / `AddJoblyWorker<TContext>()` automatically configures the user's DbContext:
- Wraps the existing `DbContextOptions<TContext>` service descriptor to add row-lock interceptors
- Replaces `IModelCustomizer` with `JoblyModelCustomizer` to auto-apply entity configurations
- Registers `TimeProvider.System` via `TryAddSingleton` (overridable for testing)
- Users just register their DbContext normally — no manual configuration needed

### Worker Groups

Workers can be split into groups with independent queues and polling intervals. Top-level `WorkerCount`/`Queues`/`PollingInterval` become the first implicit group. `AddWorkerGroup()` adds additional groups. Default `WorkerCount = Math.Min(Environment.ProcessorCount * 5, 20)`.
