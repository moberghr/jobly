# Jobly

> It gets the job done.

A distributed job processing, message queue, and in-memory mediator library for .NET 10. Three patterns, one unified pipeline, a real-time dashboard.

## Features

- **Background Jobs** — Schedule and orchestrate jobs with retries, continuations, and batch processing.
- **Message Queue** — Publish messages with multiple handlers. Each handler runs as an independent, retryable job.
- **In-Memory Requests** — `IRequest<TResponse>` with `IMediator.Send()` for immediate, typed request/response. No database persistence.
- **Unified Pipeline** — `IPipelineBehavior<T, TResponse>` wraps all three patterns (jobs, messages, requests).
- **Named Queues** — Assign jobs to queues. Workers subscribe to specific queues. Alphabetical order = priority.
- **Execution Logs** — ILogger output automatically captured during handler execution, viewable in dashboard. Each log entry tracks which worker produced it.
- **Unified Activity Log** — Single audit trail per job: lifecycle events (Created, Processing, Completed, Failed, Cancelled) + handler logs.
- **Multi-Database** — PostgreSQL and SQL Server with row-level locking for concurrent worker safety.
- **Server Monitoring** — Worker registration, heartbeat tracking, orphaned job recovery. Worker detail page shows job activity.
- **Job Retention** — Configurable `JobExpirationTimeout` (default 1 day). Optional `MaxExpirableJobCount` threshold. Failed jobs persist forever.
- **Time-Series Stats** — Hourly succeeded/failed counts for historical graphs.
- **Recurring Jobs** — Cron-based scheduled job execution. Immutable execution history via RecurringJobLog.
- **Graceful Cancellation** — CancellationMode enum signals handlers to stop. Job stays in Processing with "Cancelling..." badge until handler exits. Handlers that complete despite cancellation are marked Completed.
- **Failed Job Type Filter** — Group failed jobs by type, filter, and bulk delete/requeue all of a specific type.
- **Dashboard Auth** — Pluggable `IJoblyAuthorizationFilter` with optional redirect URL. Ships with `LocalRequestsOnlyAuthorizationFilter`.
- **Dashboard** — React-based web UI with realtime graph, historical graph, dark mode, clickable metric cards, bulk actions, batch progress bars, worker detail page.
- **TimeProvider** — All production code uses injectable `TimeProvider` for testability.

## Integration Guide

### 1. Register Services

Register your DbContext as usual — Jobly hooks into it automatically when you call `AddJobly` or `AddJoblyWorker`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register your DbContext (Jobly adds interceptors and entity configuration automatically)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Register Jobly worker (includes AddJobly internally)
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    options.WorkerCount = 5;
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.Queues = ["critical", "default", "low"];
    options.JobExpirationTimeout = TimeSpan.FromDays(7);
});

// Scan assembly for IJobHandler<T> and IMessageHandler<T> implementations
builder.Services.AddJobHandlers(typeof(Program).Assembly);

var app = builder.Build();

// Dashboard UI (serves at /jobly)
app.UseJoblyUI();

app.Run();
```

If you only need to publish jobs (no worker), use `AddJobly` instead:

```csharp
builder.Services.AddJobly<AppDbContext>();
```

For fine-grained control, use worker groups to assign different queues and polling intervals:

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    options.WorkerCount = 5;
    options.Queues = ["critical"];
    options.PollingInterval = TimeSpan.FromMilliseconds(100);

    options.AddWorkerGroup(group =>
    {
        group.WorkerCount = 2;
        group.Queues = ["reports", "default"];
        group.PollingInterval = TimeSpan.FromSeconds(5);
    });
});
```

### 2. Define Messages (Pub/Sub)

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

### 3. Define Jobs (Orchestration)

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
await publisher.Enqueue(new GenerateReport { Month = 3 }, maxRetries: 3);
await publisher.Enqueue(new GenerateReport { Month = 3 }, queue: "reports");
await publisher.Enqueue(new FollowUp(), parentJobId: prepareId); // continuation
```

### 4. Define Requests (In-Memory)

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

### 5. Pipeline Behaviors

The unified pipeline wraps all three patterns (jobs, messages, requests):

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

For jobs and messages, `TResponse` is `Unit`. For requests, it's your custom response type.

### 6. Named Queues

```csharp
options.Queues = ["a-critical", "b-default", "c-low"];
// Alphabetical order = priority. "a-critical" processed first.

await publisher.Enqueue(new UrgentTask(), queue: "a-critical");
await publisher.Publish(new LowPriorityEvent(), queue: "c-low");
```

### 7. Recurring Jobs

```csharp
await recurringPublisher.AddOrUpdateRecurringJob(
    new CleanupSessions(), name: "session-cleanup", cron: "0 * * * *");
```

`AddOrUpdateRecurringJob` registers the definition. The `RecurringJobSchedulerTask` creates jobs when the cron time arrives. Execution history is tracked in `RecurringJobLog` and survives job cleanup.

### 8. Dashboard Authorization

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

### 9. Configuration

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
});
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
│   └── Jobly.Tests/         # 220 tests (xUnit + Shouldly + Testcontainers + Respawn)
├── tests/
│   ├── Jobly.Test.Shared/   # Shared test handlers
│   ├── Jobly.TestApp/       # Test web application with login page
│   └── Jobly.TestWorker/    # Test worker service
└── ui/                      # Vite + React + TypeScript + Tailwind + shadcn/ui
```

## Development

```bash
dotnet build Jobly.sln
dotnet test Jobly.sln --filter "Category!=SqlServer"  # PostgreSQL only (~15s)
dotnet test Jobly.sln                                   # Both databases (~30s)
cd src/ui && npm run dev                                # Dashboard on :5173
```

Requires Docker for tests (Testcontainers + Respawn).
