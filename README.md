# Jobly

A distributed job processing and message queue library for .NET 10. Supports pub/sub messaging, orchestrated background jobs, and a web dashboard — all with a unified pipeline.

## Features

- **Message Queue** — Publish messages with multiple handlers. Each handler runs as an independent, retryable job.
- **Background Jobs** — Schedule and orchestrate jobs with retries, continuations, and batch processing.
- **Named Queues** — Assign jobs to queues. Workers subscribe to specific queues. Alphabetical order = priority.
- **Pipeline Behaviors** — Middleware chain wraps all handler executions (logging, validation, error handling).
- **Execution Logs** — ILogger output automatically captured during handler execution, viewable in dashboard.
- **Unified Activity Log** — Single audit trail per job: lifecycle events (Created, Processing, Completed, Failed) + handler logs.
- **Multi-Database** — PostgreSQL and SQL Server with row-level locking for concurrent worker safety.
- **Server Monitoring** — Worker registration, heartbeat tracking, orphaned job recovery.
- **Job Retention** — Auto-expiration for completed jobs. Failed jobs persist forever. Statistics survive deletion.
- **Time-Series Stats** — Hourly succeeded/failed counts for historical graphs.
- **Recurring Jobs** — Cron-based scheduled job execution.
- **Dashboard** — React-based web UI with realtime graph (jobs/sec), historical graph (24h), dark mode, bulk actions, per-page selector, Hangfire-style job detail with colored state cards.

## Integration Guide

### 1. Set Up Your DbContext

```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddOutboxStateEntity(); // Add Jobly tables
    }
}
```

### 2. Register Services

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
           .AddJoblyInterceptors());

builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    options.WorkerCount = 5;
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.Queues = new[] { "critical", "default", "low" };
    options.JobExpirationTimeout = TimeSpan.FromDays(7);
});

builder.Services.AddJobHandlers(typeof(Program).Assembly);

var app = builder.Build();
app.UseJoblyUI(); // Dashboard
app.Run();
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
await publisher.Enqueue(new GenerateReport { Month = 3 }, maxRetries: 3);
await publisher.Enqueue(new GenerateReport { Month = 3 }, queue: "reports");
await publisher.Enqueue(new FollowUp(), parentJobId: prepareId); // continuation
```

### 5. Pipeline Behaviors

```csharp
public class LoggingBehavior<T> : IPipelineBehavior<T> where T : class
{
    private readonly ILogger<LoggingBehavior<T>> _logger;

    public async Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Handling {Type}", typeof(T).Name);
        try { await next(); }
        finally { _logger.LogInformation("Handled {Type} in {Ms}ms", typeof(T).Name, sw.ElapsedMilliseconds); }
    }
}

builder.Services.AddPipelineBehaviors(typeof(Program).Assembly);
```

### 6. Named Queues

```csharp
options.Queues = new[] { "a-critical", "b-default", "c-low" };
// Alphabetical order = priority. "a-critical" processed first.

await publisher.Enqueue(new UrgentTask(), queue: "a-critical");
await publisher.Publish(new LowPriorityEvent(), queue: "c-low");
```

### 7. Recurring Jobs

```csharp
await recurringPublisher.AddOrUpdateRecurringJob(
    new CleanupSessions(), name: "session-cleanup", cron: "0 * * * *");
```

## How It Works

### Message Flow
```
Publish(OrderCreated) → Message (Enqueued)
  ↓ Worker routes
  → Job 1 (SendEmail)     → Completed → stats:succeeded +1
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

### Concurrency Safety
- **Job pickup**: `FOR UPDATE SKIP LOCKED` — one worker per job
- **State changes**: Transaction + row lock — stats and state atomic
- **Bulk operations**: Per-job transactions — failures skip, don't propagate
- **Message routing**: Row lock prevents duplicate fan-out

### Job Retention
- Completed/Deleted: auto-expire (configurable TTL)
- Failed: never expire (manual intervention required)
- Statistics survive deletion (persistent counters)
- Hourly stats cleaned up after 7 days

## Dashboard

- **Realtime graph** — jobs/second, polling every 2s, rolling 5 minutes
- **Historical graph** — succeeded/failed per hour, last 24 hours
- **Metric cards** — current (enqueued, processing, failed, etc.) + historical totals
- **Job list** — by state, bulk actions (requeue/delete), per-page selector
- **Job detail** — Hangfire-style colored state cards with duration, handler output, flow visualization
- **Messages** — list + detail with spawned jobs
- **Recurring jobs** — cron schedules, trigger/remove
- **Servers** — health, workers, current jobs
- **Dark mode** — system preference + toggle

## Project Structure

```
src/
├── core/
│   ├── Jobly.Core/          # Entities, handlers, publisher, services, logging
│   ├── Jobly.Worker/        # Worker service, health manager, worker setup
│   ├── Jobly.UI/            # Dashboard API endpoints
│   └── Jobly.Tests/         # 120 integration tests (Respawn + Testcontainers)
├── tests/
│   ├── Jobly.Test.Shared/   # Shared test handlers
│   ├── Jobly.TestApp/       # Test web application
│   └── Jobly.TestWorker/    # Test worker service
└── ui/                      # Vite + React + Tailwind + shadcn/ui + Recharts
```

## Development

```bash
cd src && dotnet build Jobly.sln
dotnet test Jobly.sln --filter "Category!=SqlServer"  # ~10 seconds
cd src/ui && npm run dev                               # Dashboard on :5173
```

Requires Docker for tests (Testcontainers + Respawn).
