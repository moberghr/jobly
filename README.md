# Jobly

> It gets the job done.

A distributed job processing, message queue, and in-memory mediator library for .NET 10. Four patterns, one unified pipeline, a real-time dashboard.

[![NuGet](https://img.shields.io/nuget/v/Moberg.Jobly.Core?label=Jobly.Core)](https://www.nuget.org/packages/Moberg.Jobly.Core)
[![NuGet](https://img.shields.io/nuget/v/Moberg.Jobly.Worker?label=Jobly.Worker)](https://www.nuget.org/packages/Moberg.Jobly.Worker)
[![NuGet](https://img.shields.io/nuget/v/Moberg.Jobly.UI?label=Jobly.UI)](https://www.nuget.org/packages/Moberg.Jobly.UI)
[![Docs](https://img.shields.io/badge/docs-moberghr.github.io%2Fjobly-blue)](https://moberghr.github.io/jobly/)

## Features

- **Background Jobs** — Schedule and orchestrate jobs with retries, continuations, and batch processing.
- **Message Queue** — Publish messages with multiple handlers. Each handler runs as an independent, retryable job.
- **In-Memory Requests** — `IRequest<TResponse>` with `IMediator.Send()` for immediate, typed request/response. No database persistence.
- **In-Memory Streams** — `IStreamRequest<TResponse>` with `IMediator.CreateStream()` for lazy, item-by-item streaming via `IAsyncEnumerable<TResponse>`. No database persistence.
- **Unified Pipeline** — `IPipelineBehavior<T, TResponse>` wraps all four patterns. `IStreamPipelineBehavior<T, TResponse>` adds enumeration-level wrapping for streams.
- **Named Queues** — Assign jobs to queues. Workers subscribe to specific queues. Alphabetical order = priority.
- **Execution Logs** — ILogger output automatically captured and flushed to the database every ~1 second during handler execution, viewable in dashboard in real time. Each log entry tracks which worker produced it.
- **Unified Activity Log** — Single audit trail per job: lifecycle events (Created, Processing, Completed, Failed, Cancelled) + handler logs.
- **Naming Convention Support** — Entity configurations respect EF Core naming conventions (e.g., `UseSnakeCaseNamingConvention()`). All Jobly tables default to the `jobly` schema (configurable via `JoblyConfiguration.Schema`).
- **Multi-Database** — PostgreSQL and SQL Server with row-level locking for concurrent worker safety.
- **Server Monitoring** — Worker registration, heartbeat tracking, orphaned job recovery. Worker detail page shows job activity.
- **Job Retention** — Configurable `JobExpirationTimeout` (default 1 day). Optional `MaxExpirableJobCount` threshold. Failed jobs persist forever.
- **Time-Series Stats** — Hourly succeeded/failed counts for historical graphs.
- **Recurring Jobs** — Cron-based scheduled job execution. Immutable execution history via RecurringJobLog.
- **Graceful Cancellation** — CancellationMode enum signals handlers to stop. Job stays in Processing with "Cancelling..." badge until handler exits. Handlers that complete despite cancellation are marked Completed.
- **Pause / Resume** — Pause and resume job processing at the server or worker group level. Paused workers stop picking up new jobs; in-progress jobs continue to completion.
- **Job Metadata** — Attach key-value metadata to jobs via `JobParameters.Metadata`. Metadata inherited by child jobs, accessible in handlers via `IJobContext`. Publish pipeline behaviors (`IPublishPipelineBehavior<T>`) for cross-cutting metadata.
- **Real-time Handler Logs** — ILogger output flushed to the database every ~1 second during handler execution, visible in dashboard while the job is still processing.
- **Failed Job Type Filter** — Group failed jobs by type, filter, and bulk delete/requeue all of a specific type.
- **Dashboard Auth** — Pluggable `IJoblyAuthorizationFilter` with optional redirect URL. Ships with `LocalRequestsOnlyAuthorizationFilter`.
- **Configurable Handler Logging** — `EnableHandlerLogging` option (default true) to suppress handler ILogger output from the JobLog table when not needed.
- **Dashboard** — React-based web UI with realtime graph, historical graph, dark mode, clickable metric cards, bulk actions, batch progress bars, worker detail page.
- **TimeProvider** — All production code uses injectable `TimeProvider` for testability.
- **DB Push (optional)** — Opt-in `opt.UseDatabasePush()` replaces polling wake-up with PostgreSQL `LISTEN`/`NOTIFY` or SQL Server Service Broker, cutting dispatcher pickup latency from ~500ms to <50ms without tight polling.

## Integration Guide

### 1. Install packages

Jobly ships as a small set of NuGet packages — pick the provider package that matches your database:

| Package                    | Purpose                                               |
|----------------------------|-------------------------------------------------------|
| `Moberg.Jobly.Core`        | Core types (always required)                          |
| `Moberg.Jobly.Worker`      | Worker + background tasks (required for processing)   |
| `Moberg.Jobly.PostgreSql`  | PostgreSQL provider (row-lock SQL, LISTEN/NOTIFY, locks) |
| `Moberg.Jobly.SqlServer`   | SQL Server provider (row-lock SQL, Service Broker, locks) |
| `Moberg.Jobly.UI`          | Dashboard UI (optional)                               |

You only add the provider package for your database; Jobly.Core no longer has a hard dependency on either EF provider.

### 2. Register Services

Register your DbContext as usual — Jobly hooks into it automatically when you call `AddJobly` or `AddJoblyWorker`. Opt into a provider from the lambda:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register your DbContext (Jobly adds interceptors and entity configuration automatically)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Register Jobly worker (includes AddJobly internally).
// opt.UsePostgreSql() comes from Moberg.Jobly.PostgreSql and registers the row-lock SQL,
// distributed lock provider, exception classifier, and the push notification factory.
builder.Services.AddJoblyWorker<AppDbContext>(opt =>
{
    opt.UsePostgreSql();

    opt.WorkerCount = 5;
    opt.PollingInterval = TimeSpan.FromSeconds(1);
    opt.Queues = ["critical", "default", "low"];
    opt.JobExpirationTimeout = TimeSpan.FromDays(7);

    // Core addons live on the same builder
    opt.AddRetry(r => r.MaxRetries = 3);
    opt.AddMutex();
});

// Scan assembly for IJobHandler<T> and IMessageHandler<T> implementations
builder.Services.AddHandlers(typeof(Program).Assembly);

var app = builder.Build();

// Dashboard UI (serves at /jobly)
app.UseJoblyUI();

app.Run();
```

If you only need to publish jobs (no worker), use `AddJobly` instead:

```csharp
builder.Services.AddJobly<AppDbContext>(opt =>
{
    opt.UsePostgreSql();  // or opt.UseSqlServer()
});
```

For fine-grained control, use worker groups to assign different queues and polling intervals:

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(opt =>
{
    opt.UseSqlServer();

    opt.WorkerCount = 5;
    opt.Queues = ["critical"];
    opt.PollingInterval = TimeSpan.FromMilliseconds(100);

    opt.AddWorkerGroup(group =>
    {
        group.WorkerCount = 2;
        group.Queues = ["reports", "default"];
        group.PollingInterval = TimeSpan.FromSeconds(5);
    });
});
```

### 3. Define Messages (Pub/Sub)

```csharp
public class OrderCreated : IMessage
{
    public int OrderId { get; set; }
    public string CustomerEmail { get; set; }
}

public class SendConfirmationEmail : IMessageHandler<OrderCreated>
{
    private readonly ILogger<SendConfirmationEmail> _logger;

    public SendConfirmationEmail(ILogger<SendConfirmationEmail> logger) => _logger = logger;

    public async Task HandleAsync(OrderCreated message, CancellationToken ct)
    {
        _logger.LogInformation("Sending email for order {OrderId}", message.OrderId);
        // All ILogger calls are captured and viewable in the dashboard
    }
}

public class UpdateInventory : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, CancellationToken ct)
    {
        // Runs independently — if email fails, this still succeeds
    }
}
```

**Publish with outbox pattern:**

```csharp
[HttpPost]
public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
{
    var order = new Order { /* ... */ };
    _context.Orders.Add(order);

    // Same transaction — if SaveChanges fails, no message is sent
    await _publisher.Publish(new OrderCreated { OrderId = order.Id });

    await _context.SaveChangesAsync();
    return Ok(order.Id);
}
```

### 4. Define Jobs (Orchestration)

```csharp
public class GenerateReport : IJob
{
    public int Month { get; set; }
}

public class GenerateReportHandler : IJobHandler<GenerateReport>
{
    private readonly ILogger<GenerateReportHandler> _logger;

    public async Task HandleAsync(GenerateReport message, CancellationToken ct)
    {
        _logger.LogInformation("Generating report for month {Month}", message.Month);
        // Handler logs appear in the job's Activity Log on the dashboard
    }
}
```

**Publishing options:**

```csharp
await publisher.Enqueue(new GenerateReport { Month = 3 });
await publisher.Schedule(new GenerateReport { Month = 3 }, tomorrow);
await publisher.Enqueue(new GenerateReport { Month = 3 }, queue: "reports");
await publisher.Enqueue(new FollowUp(), parentJobId: prepareId); // continuation
```

### 5. Define Requests (In-Memory)

```csharp
public class GetUser : IRequest<UserDto>
{
    public int UserId { get; set; }
}

public class GetUserHandler : IRequestHandler<GetUser, UserDto>
{
    public async Task<UserDto> HandleAsync(GetUser request, CancellationToken ct)
    {
        return await _db.Users.FindAsync(request.UserId, ct);
    }
}
```

**Send via IMediator:**

```csharp
var user = await mediator.Send(new GetUser { UserId = 1 });
```

Requests execute immediately in-process — no database, no worker, no retries. Errors bubble up to the caller.

### 6. Define Streams (In-Memory)

```csharp
public class GetUsers : IStreamRequest<UserDto>
{
    public string Role { get; set; }
}

public class GetUsersHandler : IStreamRequestHandler<GetUsers, UserDto>
{
    public async IAsyncEnumerable<UserDto> HandleAsync(GetUsers request, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var user in _db.Users.AsAsyncEnumerable().WithCancellation(ct))
        {
            yield return new UserDto { Id = user.Id, Name = user.Name };
        }
    }
}
```

**Stream via IMediator:**

```csharp
await foreach (var user in mediator.CreateStream(new GetUsers { Role = "Admin" }))
{
    Console.WriteLine(user.Name);
}
```

Streams execute lazily in-process — items are yielded one at a time. No database persistence, no worker, no retries.

### 7. Pipeline Behaviors

The unified pipeline wraps all four patterns — jobs, messages, requests, and streams:

```csharp
public class LoggingBehavior<T, TResponse> : IPipelineBehavior<T, TResponse>
    where T : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<T, TResponse>> _logger;

    public async Task<TResponse> HandleAsync(T request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Handling {Type}", typeof(T).Name);
        var result = await next();
        _logger.LogInformation("Handled {Type} in {Ms}ms", typeof(T).Name, sw.ElapsedMilliseconds);
        return result;
    }
}

builder.Services.AddPipelineBehaviors(typeof(Program).Assembly);
```

For jobs and messages, `TResponse` is `Unit`. For requests, it's your custom response type. For streams, it's `IAsyncEnumerable<T>`.

### 8. Named Queues

```csharp
options.Queues = ["a-critical", "b-default", "c-low"];
// Alphabetical order = priority. "a-critical" processed first.

await publisher.Enqueue(new UrgentTask(), queue: "a-critical");
await publisher.Publish(new LowPriorityEvent(), queue: "c-low");
```

### 9. Recurring Jobs

```csharp
await recurringPublisher.AddOrUpdateRecurringJob(
    new CleanupSessions(), name: "session-cleanup", cron: "0 * * * *");
```

`AddOrUpdateRecurringJob` registers the definition. The `RecurringJobSchedulerTask` creates jobs when the cron time arrives. Execution history is tracked in `RecurringJobLog` and survives job cleanup.

### 10. Dashboard Authorization

```csharp
app.UseJoblyUI(options =>
{
    options.Authorization = new MyAuthFilter();
    options.UnauthorizedRedirectUrl = "/login"; // optional, redirects browser requests
});

public class MyAuthFilter : IJoblyAuthorizationFilter
{
    public bool Authorize(HttpContext httpContext)
    {
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole("Admin");
    }
}
```

Built-in filter for localhost-only access:

```csharp
options.Authorization = new LocalRequestsOnlyAuthorizationFilter();
```

### 10. Configuration

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    // Worker
    options.WorkerCount = 10;
    options.Queues = ["default"];
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.UseDispatcher = false; // true = batch-fetch mode

    // Cancellation
    options.CancellationCheckInterval = TimeSpan.FromSeconds(5);
    options.InvisibilityTimeout = TimeSpan.FromMinutes(5);

    // Retention
    options.JobExpirationTimeout = TimeSpan.FromDays(1);
    options.MaxExpirableJobCount = 20_000; // null to disable
    options.ExpirationBatchSize = 1000;

    // Background tasks
    options.OrchestrationInterval = TimeSpan.FromSeconds(10);
    options.MessageRoutingInterval = TimeSpan.FromSeconds(1);
    options.HealthCheckInterval = TimeSpan.FromSeconds(10);
    options.CounterAggregationInterval = TimeSpan.FromSeconds(5);

    // Scheduled-job activation. Future-dated jobs (`Schedule(job, at)`) sit in `State.Scheduled`
    // until this task flips them to `Enqueued`. The interval is the worst-case lag between
    // `ScheduleTime` and the job becoming pickup-eligible — e.g., default 5s means a job
    // scheduled for 12:00:00 runs somewhere in [12:00:00, 12:00:05]. Lower this for tighter
    // scheduled-job latency; the task is time-driven (no event trigger exists for "time has
    // passed"), so push notifications don't help here.
    options.ScheduledActivationInterval = TimeSpan.FromSeconds(5);
});
```

### 11. DB Push (optional)

Replaces polling wake-up with push notifications — PostgreSQL `LISTEN`/`NOTIFY` or SQL Server Service Broker. The dispatcher, `MessageRoutingTask`, and `OrchestrationTask` wake instantly on relevant events instead of waiting for their next poll. Opt-in; default behavior (polling) is unchanged if you don't call `opt.UseDatabasePush()`.

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(opt =>
{
    opt.UsePostgreSql();         // or opt.UseSqlServer()

    // Push benefits the dispatcher's batch-fetch path; individual workers still poll.
    opt.UseDispatcher = true;
    opt.PollingInterval = TimeSpan.FromSeconds(5); // loose polling is fine when push is on

    opt.UseDatabasePush();
});
```

The provider-specific transport is wired by whichever `UsePostgreSql()` / `UseSqlServer()` you called. Transports are resilient to connection drops — the listener reconnects with exponential backoff and fires a drain signal on every reconnect so jobs enqueued during the gap are picked up.

**Scheduled jobs**: push accelerates *immediate* enqueues. Jobs published via `Schedule(job, at)` sit in `State.Scheduled` until `ScheduledJobActivationTask` flips them to `Enqueued` — only then does the `JobEnqueued` notification fire. Dispatcher pickup after activation is <50ms via push, but the activation itself is time-driven and bounded by `ScheduledActivationInterval` (default 5s, see §10). If you need sub-second precision on scheduled jobs, lower that interval — polling is the only mechanism, since there's no event for "wall-clock time has advanced."

**SQL Server setup requirements**: Service Broker must be enabled on the target database. Jobly creates the message type / contract / queue / service idempotently on first publish, but it cannot run `ALTER DATABASE ... SET ENABLE_BROKER` for you (that requires exclusive DB access). If broker isn't enabled, the transport logs an actionable error and degrades silently to polling:

```sql
ALTER DATABASE [YourDb] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
```

**Observability**: transport failures are logged at Warning and incremented on `jobly.notifications.publish_failures` (OpenTelemetry counter). Successful publishes increment `jobly.notifications.published`. Alert on the failure counter if push health matters to your SLOs.

**Upgrading from <0.9**: the `Scheduled` state was introduced alongside DB push. Future-dated jobs published on the old codebase land in `Enqueued` with `ScheduleTime > now` and are correctly gated by a defensive predicate in worker queries — but they won't appear in the dashboard's Scheduled list until their time arrives. To migrate them eagerly, run once after upgrade:

```sql
UPDATE jobly.job
SET    current_state = 7  -- State.Scheduled
WHERE  current_state = 1  -- State.Enqueued
  AND  schedule_time > now();
```

## How It Works

### Message Flow
```
Publish(OrderCreated) → Message (Enqueued)
  ↓ MessageRoutingTask routes
  → Job 1 (SendEmail)       → Completed → stats:succeeded +1
  → Job 2 (UpdateInventory) → Completed → stats:succeeded +1
  → Message Completed → ExpireAt set
```

### Job Flow
```
Enqueue(GenerateReport) → Job (Enqueued, queue="reports")
  ↓ Worker picks up (queue match + schedule)
  → Pipeline → Handler → ILogger captured → Completed
  → stats:succeeded +1, stats:succeeded:{hour} +1
  → ExpireAt set → eventually cleaned up
```

### Request Flow
```
mediator.Send(GetUser { Id = 1 })
  → Resolve IRequestHandler<GetUser, UserDto>
  → Pipeline behaviors (logging, validation, etc.)
  → Handler.HandleAsync → UserDto returned
  → No database involved
```

### Stream Flow
```
mediator.CreateStream(GetUsers { Role = "Admin" })
  → IPipelineBehavior chain (request-level: auth, logging)
  → IStreamPipelineBehavior chain (enumeration-level)
  → IStreamRequestHandler.HandleAsync → IAsyncEnumerable<UserDto>
  → Items yielded lazily on enumeration
  → No database involved
```

### Cancellation Flow
```
DeleteJob(processingJobId)
  → CancellationMode = Graceful (state stays Processing)
  → RunJobMonitor detects CancellationMode
  → Handler's CancellationToken cancelled
  → If handler stops: state → Deleted
  → If handler completes anyway: state → Completed
```

### Concurrency Safety
- **Job pickup**: `FOR UPDATE SKIP LOCKED` — one worker per job
- **State changes**: Transaction + row lock — stats and state atomic
- **Bulk operations**: Per-job transactions — failures skip, don't propagate
- **Message routing**: Row lock prevents duplicate fan-out

### Job Retention
- Completed/Deleted: auto-expire (configurable via `JobExpirationTimeout`)
- Failed: never expire (manual intervention required)
- Count-based: optional `MaxExpirableJobCount` deletes oldest by ExpireAt
- Statistics survive deletion (persistent counters)
- Hourly stats cleaned up after 7 days
- Recurring job logs: last 100 per recurring job retained

## Dashboard

- **Realtime graph** — jobs/second, polling every 2s, rolling 5 minutes
- **Historical graph** — succeeded/failed per hour, 24h or 7d view
- **Metric cards** — clickable, navigate to corresponding pages
- **Job list** — by state, bulk actions (requeue/delete), per-page selector
- **Failed jobs** — type filter bar, bulk delete/requeue by type
- **Job detail** — colored state cards with duration, handler output, "Cancelling..." badge
- **Messages** — list with job count, detail with spawned jobs
- **Batches** — stacked green/red progress bar (completed/failed)
- **Recurring jobs** — cron schedules, trigger/delete, execution history
- **Servers** — health, CPU, memory, clickable workers
- **Worker detail** — job activity log, server link, status indicator
- **Dark mode** — system preference + toggle
- **Auth** — pluggable filter with optional login redirect

## Project Structure

```
src/
├── core/
│   ├── Jobly.Core/          # Entities, handlers, publisher, services, logging
│   ├── Jobly.Worker/        # Worker service, background tasks, dispatcher
│   ├── Jobly.UI/            # Dashboard API endpoints + embedded SPA
│   └── Jobly.Tests/         # 632 tests (xUnit + Shouldly + Testcontainers + Respawn)
├── tests/
│   ├── Jobly.Test.Shared/   # Shared test handlers
│   ├── Jobly.TestApp/       # Test web application with login page
│   └── Jobly.TestWorker/    # Test worker service
└── ui/                      # Vite + React + TypeScript + Tailwind + shadcn/ui
```

## Development

```bash
dotnet build Jobly.slnx
dotnet test Jobly.slnx --filter "Category!=SqlServer"  # PostgreSQL only (~45s)
dotnet test Jobly.slnx                                   # Both databases (~80s)
cd src/ui && npm run dev                                # Dashboard on :5173
```

Requires Docker for tests (Testcontainers + Respawn).
