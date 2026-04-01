# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Jobly

Jobly is a distributed job processing and message queue library for .NET 10. Two patterns:

- **Messages** (`IMessage`) — Pub/sub queue. Multiple handlers per message, each an independent job.
- **Jobs** (`IJob`) — Orchestrated background work. Single handler, scheduling, retries, continuations, batches, named queues.

Ships as NuGet packages (Jobly.Core, Jobly.UI, Jobly.Worker). Supports PostgreSQL and SQL Server.

## Build & Test Commands

```bash
# Backend (from src/)
dotnet build Jobly.sln
dotnet test Jobly.sln --filter "Category!=SqlServer"   # PostgreSQL only
dotnet test Jobly.sln                                    # All databases (432 tests, ~30s)

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
```

### Handler Interfaces

```csharp
public interface IMessageHandler<in T> where T : IMessage { Task HandleAsync(T message, CancellationToken ct); }
public interface IJobHandler<in T> where T : IJob { Task HandleAsync(T message, CancellationToken ct); }
public interface IPipelineBehavior<in T> where T : class { Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken ct); }
```

### Job Entity

Single table with `Kind` enum. Key fields:
- **Shared**: Id, Kind, Type, Message (payload), CreateTime, CurrentState, Queue, ExpireAt, ParentJobId, TraceId, SpawnedByJobId, CancellationMode
- **Job-specific**: ScheduleTime, HandlerType, MaxRetries, RetriedTimes, CurrentWorkerId, LastKeepAlive
- **Batch-specific**: ContinuationOptions (generalized to all kinds — controls child activation on parent failure)
- **JobLog** — Unified audit trail. EventType: Created, Processing, Completed, Failed, Requeued, Deleted, Cancelled, CancellationRequested, Log (ILogger output). Includes optional `WorkerId` (set by worker-produced entries, null for command/orchestration entries).

### Worker Architecture

Workers are **pure executors** — they only fetch and execute `Kind=Job` jobs. All orchestration is handled by dedicated background tasks:

```
Worker (pure executor):
  1. Fetch Job (Kind=Job, Enqueued, ScheduleTime < now)
  2. Execute handler
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

### Recurring Jobs

- `AddOrUpdateRecurringJob` only registers/updates the definition (cron, message, type). **Does not create jobs.**
- `RecurringJobSchedulerTask` creates jobs with `ScheduleTime = now` (ready for immediate execution) and sets `NextExecution` to the next cron occurrence.
- **RecurringJobLog** — Immutable audit trail linking recurring jobs to their created jobs. Fields: `Id, RecurringJobId, JobId (nullable), CreatedAt`. `JobId` has FK with `SET NULL` cascade — when the job is cleaned up, `JobId` becomes null but the log entry survives.
- Scheduler uses RecurringJobLog for dedup: checks if the most recent log entry's job is still Enqueued/Processing.
- ExpirationCleanupTask retains last 100 logs per recurring job (uses `HAVING COUNT > 100` to skip most).

### Key Design Decisions

- **No raw SQL** — all queries use EF Core LINQ. No `_context.Set<>()` subqueries inside `.Select()` projections — use navigation properties or two-step fetch instead.
- **TimeProvider** — all production code uses injectable `TimeProvider` instead of `DateTime.UtcNow`. Registered as `TryAddSingleton(TimeProvider.System)` in `AddJobly`. Test code can use `DateTime.UtcNow` directly.
- **DbContext must be registered as Scoped** (not Transient). The outbox pattern requires the publisher and application code to share the same DbContext instance within a scope.
- Everything is a Job. `ParentJobId` replaces both the old MessageId and BatchId foreign keys.
- Workers never touch parent/child orchestration — that's the OrchestrationTask's job.
- `ContinuationOptions` is generalized: any job with children can control whether children activate on failure.
- Failed jobs never auto-deleted.
- Statistics use Counter rows (write-optimized) aggregated into Statistic rows by CounterAggregatorTask.
- All background tasks re-run immediately if work was found (ServerTaskBase pattern).
- `RequeueJob` resets `ScheduleTime` to now — requeued jobs always execute immediately.
- Count-based cleanup: `MaxExpirableJobCount` (default 20k) — deletes oldest by `ExpireAt` when threshold exceeded. Failed jobs excluded (null `ExpireAt`).

### Backend (.NET 10)

- **Jobly.Core** — Entities (Job, RecurringJob, RecurringJobLog, JobLog, Server, Worker, ServerTask, ServerLog), handlers, JobDispatcher (cached reflection), Publisher, BatchPublisher, logging (JobLogContext/JobLoggerProvider). Services: `JobQueryService`, `JobCommandService`, `JobGroupQueryService`, `RecurringJobService`, `DashboardStatsService`.
- **Jobly.Worker** — JoblyWorkerService (pure executor), JoblyDispatcher/JoblyDispatcherWorker (batch-fetch mode), worker groups. Background tasks: HeartbeatTask, CounterAggregatorTask, ServerCleanupTask, StaleJobRecoveryTask, ExpirationCleanupTask, RecurringJobSchedulerTask, MessageRoutingTask, OrchestrationTask.
- **Jobly.UI** — Minimal API endpoints + embedded SPA served at `/jobly`.
- **Static analyzers** — StyleCop, Roslynator, SonarAnalyzer, Meziantou.

### Frontend (Vite + React 18 + TypeScript)

`src/ui/`. Tailwind + shadcn/ui, Zustand, Axios. Dashboard with realtime + historical graphs, job list by state with bulk actions, dark mode, job detail with colored state cards + handler output, messages, batches, recurring jobs, servers, worker detail page. Cancel button shows "Cancelling..." badge for processing jobs. Failed jobs page has type filter with bulk delete/requeue by type. Batch progress bar shows stacked green/red (completed/failed).

## Testing

432 tests (216 PostgreSQL + 216 SQL Server) using xUnit, Shouldly, Testcontainers + Respawn (~30s).

### Test Structure

Two categories with **separate databases** (no interference):

**Unit tests** (`src/core/Jobly.Tests/Unit/`) — ~380 tests, ~14s:
- Each test calls exactly ONE public method on ONE class
- State set up via direct DB inserts, not via other services
- Fresh `CreateContext()` for setup, act, and assert (no shared tracking)
- Use `[Collection("PostgreSql")]` / `[Collection("SqlServer")]` fixtures (no server running)

**Integration tests** (`src/core/Jobly.Tests/Integration/`) — ~52 tests, ~16s:
- Use `JoblyTestServer` — boots full worker + all background tasks against a real database
- Tests publish jobs, wait for results via `Server.WaitForCompletion()` / `Server.WaitForJobState()`
- Use `[Collection("PostgreSql-Integration")]` / `[Collection("SqlServer-Integration")]` fixtures (server running)
- Server shared per collection fixture (boots once, Respawn between tests with retry on lock contention)

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
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
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

### Registration

`AddJobly<TContext>()` / `AddJoblyWorker<TContext>()` automatically configures the user's DbContext:
- Wraps the existing `DbContextOptions<TContext>` service descriptor to add row-lock interceptors
- Replaces `IModelCustomizer` with `JoblyModelCustomizer` to auto-apply entity configurations
- Registers `TimeProvider.System` via `TryAddSingleton` (overridable for testing)
- Users just register their DbContext normally — no manual configuration needed

### Worker Groups

Workers can be split into groups with independent queues and polling intervals. Top-level `WorkerCount`/`Queues`/`PollingInterval` become the first implicit group. `AddWorkerGroup()` adds additional groups. Default `WorkerCount = Math.Min(Environment.ProcessorCount * 5, 20)`.
