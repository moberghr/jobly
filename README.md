# Jobly

A distributed job processing and message queue library for .NET 10. Supports pub/sub messaging, orchestrated background jobs, and a web dashboard — all with a unified pipeline.

## Features

- **Message Queue** — Publish messages with multiple handlers. Each handler runs as an independent, retryable job.
- **Background Jobs** — Schedule and orchestrate jobs with retries, continuations, and batch processing.
- **Named Queues** — Assign jobs to queues. Workers subscribe to specific queues. Alphabetical order = priority.
- **Pipeline Behaviors** — Middleware chain wraps all handler executions (logging, validation, error handling).
- **Execution Logs** — ILogger output automatically captured during handler execution, viewable in dashboard.
- **Multi-Database** — PostgreSQL and SQL Server with row-level locking for concurrent worker safety.
- **Server Monitoring** — Worker registration, heartbeat tracking, orphaned job recovery.
- **Job Retention** — Auto-expiration for completed jobs. Failed jobs persist forever. Statistics survive deletion.
- **Recurring Jobs** — Cron-based scheduled job execution.
- **Dashboard** — React-based web UI with dark mode, bulk actions, per-page selector, and live stats.

## Integration Guide

### 1. Set Up Your DbContext

Jobly stores jobs in your existing database. Add Jobly's entity configuration to your DbContext:

```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Your existing entities
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Add Jobly tables to your database
        modelBuilder.AddOutboxStateEntity();
    }
}
```

### 2. Register Services

```csharp
var builder = WebApplication.CreateBuilder(args);

// Your DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
           .AddJoblyInterceptors());  // Required: adds row locking interceptors

// Register Jobly worker
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    options.WorkerCount = 5;
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.Queues = new[] { "critical", "default", "low" };
    options.JobExpirationTimeout = TimeSpan.FromDays(7);
});

// Register handlers from your assembly
builder.Services.AddJobHandlers(typeof(Program).Assembly);

var app = builder.Build();

// Enable Jobly dashboard (optional)
app.UseJoblyUI();

app.Run();
```

### 3. Define Messages (Pub/Sub)

Use `IMessage` when one event should trigger multiple independent handlers:

```csharp
public class OrderCreated : IMessage
{
    public int OrderId { get; set; }
    public string CustomerEmail { get; set; }
}

// Each handler becomes an independent job — retryable, trackable
public class SendConfirmationEmail : IMessageHandler<OrderCreated>
{
    private readonly IEmailService _email;
    private readonly ILogger<SendConfirmationEmail> _logger;

    public SendConfirmationEmail(IEmailService email, ILogger<SendConfirmationEmail> logger)
    {
        _email = email;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreated message, CancellationToken ct)
    {
        _logger.LogInformation("Sending confirmation for order {OrderId}", message.OrderId);
        await _email.SendAsync(message.CustomerEmail, "Order confirmed", $"Order #{message.OrderId}");
    }
}

public class UpdateInventory : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, CancellationToken ct)
    {
        // Update stock levels — runs independently from email
    }
}

public class NotifyWarehouse : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, CancellationToken ct)
    {
        // Send warehouse notification — if this fails, email still succeeds
    }
}
```

**Publishing** — use the outbox pattern (jobs are created in the same transaction as your business data):

```csharp
public class OrderController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPublisher _publisher;

    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
    {
        var order = new Order { /* ... */ };
        _context.Orders.Add(order);

        // Published in the same transaction — if SaveChanges fails, no message is sent
        await _publisher.Publish(new OrderCreated
        {
            OrderId = order.Id,
            CustomerEmail = request.Email
        });

        await _context.SaveChangesAsync();
        return Ok(order.Id);
    }
}
```

### 4. Define Jobs (Orchestration)

Use `IJob` when you need scheduling, retries, continuations, or a single handler:

```csharp
public class GenerateMonthlyReport : IJob
{
    public int Year { get; set; }
    public int Month { get; set; }
}

public class GenerateMonthlyReportHandler : IJobHandler<GenerateMonthlyReport>
{
    private readonly IReportService _reports;
    private readonly ILogger<GenerateMonthlyReportHandler> _logger;

    public GenerateMonthlyReportHandler(IReportService reports, ILogger<GenerateMonthlyReportHandler> logger)
    {
        _reports = reports;
        _logger = logger;
    }

    public async Task HandleAsync(GenerateMonthlyReport message, CancellationToken ct)
    {
        _logger.LogInformation("Generating report for {Year}-{Month}", message.Year, message.Month);
        await _reports.GenerateAsync(message.Year, message.Month, ct);
        _logger.LogInformation("Report generated successfully");
        // All ILogger calls are automatically captured and viewable in the dashboard
    }
}
```

**Publishing options:**

```csharp
// Immediate execution
await publisher.Enqueue(new GenerateMonthlyReport { Year = 2026, Month = 3 });

// Scheduled for later
await publisher.Schedule(
    new GenerateMonthlyReport { Year = 2026, Month = 3 },
    new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

// With retries
await publisher.Enqueue(new GenerateMonthlyReport { Year = 2026, Month = 3 }, maxRetries: 3);

// In a specific queue
await publisher.Enqueue(new GenerateMonthlyReport { Year = 2026, Month = 3 }, queue: "reports");

// Continuation (run after parent completes)
var prepareId = await publisher.Enqueue(new PrepareReportData { Month = 3 });
await publisher.Enqueue(new GenerateMonthlyReport { Year = 2026, Month = 3 }, parentJobId: prepareId);
```

### 5. Pipeline Behaviors

Add cross-cutting concerns that wrap ALL handler executions:

```csharp
public class LoggingBehavior<T> : IPipelineBehavior<T> where T : class
{
    private readonly ILogger<LoggingBehavior<T>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<T>> logger) => _logger = logger;

    public async Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken ct)
    {
        var typeName = typeof(T).Name;
        _logger.LogInformation("Handling {MessageType}", typeName);
        var sw = Stopwatch.StartNew();

        try
        {
            await next();
            _logger.LogInformation("Handled {MessageType} in {Elapsed}ms", typeName, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed handling {MessageType} after {Elapsed}ms", typeName, sw.ElapsedMilliseconds);
            throw;
        }
    }
}

// Register pipeline behaviors
builder.Services.AddPipelineBehaviors(typeof(Program).Assembly);
```

### 6. Named Queues

Workers process queues in alphabetical order (like Hangfire). Name queues to control priority:

```csharp
// Worker configuration
options.Queues = new[] { "a-critical", "b-default", "c-low" };
// Worker checks "a-critical" first, then "b-default", then "c-low"

// Publishing to specific queues
await publisher.Enqueue(new UrgentTask(), queue: "a-critical");
await publisher.Enqueue(new RegularTask());  // defaults to "default"
await publisher.Publish(new LowPriorityEvent(), queue: "c-low");
```

### 7. Recurring Jobs

```csharp
var recurringPublisher = serviceProvider.GetRequiredService<IRecurringJobPublisher>();

// Run every hour
await recurringPublisher.AddOrUpdateRecurringJob(
    new CleanupExpiredSessions(),
    name: "session-cleanup",
    cron: "0 * * * *");

// Run every day at midnight
await recurringPublisher.AddOrUpdateRecurringJob(
    new GenerateDailySummary(),
    name: "daily-summary",
    cron: "0 0 * * *");
```

## How It Works

### Message Flow (IMessage)
```
Publish(OrderCreated) → Message row (State=Enqueued)
  ↓ Worker routes
  → Job 1 (SendConfirmationEmail)  → Executes independently
  → Job 2 (UpdateInventory)        → Executes independently
  → Job 3 (NotifyWarehouse)        → Executes independently
  ↓ All jobs complete
  → Message State = Completed → ExpireAt set → eventually cleaned up
  → Statistics persisted (stats:succeeded +3)
```

### Job Flow (IJob)
```
Enqueue(GenerateReport) → Job row (State=Enqueued, Queue="reports")
  ↓ Worker picks up (queue match + schedule time)
  → Pipeline behaviors execute
  → Handler runs (ILogger output captured)
  → Job State = Completed → ExpireAt set
  → stats:succeeded +1
```

### Concurrency Safety

All operations use database-level protection:
- **Job pickup**: Row locking (`FOR UPDATE SKIP LOCKED`) prevents duplicate processing
- **State changes**: Transaction + row lock ensures stat changes and state changes are atomic
- **Bulk operations**: Each job in its own transaction — failures skip, don't propagate
- **Message routing**: Row lock on Message prevents duplicate fan-out

### Job Retention

- Completed/Deleted jobs auto-expire after configurable TTL (default: 1 day)
- Failed jobs never auto-expire — they require manual intervention
- Persistent statistics survive job deletion (total succeeded/failed/deleted/created)
- `JoblyHealthManager` runs cleanup in batches during its health check loop

## Dashboard

The dashboard provides real-time monitoring:

- **Dashboard** — Live + historical metric cards (enqueued, processing, completed, failed, scheduled, servers, messages)
- **Jobs** — List by state with sidebar nav, bulk actions (requeue/delete), per-page selector
- **Job Detail** — State history timeline, execution logs, flow visualization (message → jobs, sibling/child relationships)
- **Messages** — Queue message list with spawned jobs
- **Recurring Jobs** — Cron schedules with trigger/remove actions
- **Servers** — Server health with worker tables
- **Dark Mode** — Toggle with system preference detection

## Project Structure

```
src/
├── core/
│   ├── Jobly.Core/          # Entities, handlers, publisher, services, logging
│   │   ├── Handlers/        # IJob, IMessage, IJobHandler, IMessageHandler, IPipelineBehavior, JobDispatcher
│   │   ├── Data/Entities/   # Job, Message, JobState, JobLog, Batch, RecurringJob, Server, Worker, Statistic
│   │   ├── Logging/         # JobLogContext, JobLoggerProvider (ILogger capture)
│   │   └── Models/          # DTOs for API responses
│   ├── Jobly.Worker/        # Background worker service, health manager, worker setup
│   ├── Jobly.UI/            # Dashboard API endpoints
│   └── Jobly.Tests/         # 108 integration tests (Respawn + Testcontainers)
├── tests/
│   ├── Jobly.Test.Shared/   # Shared test handlers and configuration
│   ├── Jobly.TestApp/       # Test web application
│   └── Jobly.TestWorker/    # Test worker service
└── ui/                      # Vite + React + Tailwind + shadcn/ui dashboard
```

## Development

```bash
# Build
cd src && dotnet build Jobly.sln

# Test (PostgreSQL only — ~9 seconds with Respawn)
dotnet test Jobly.sln --filter "Category!=SqlServer"

# Test (all databases)
dotnet test Jobly.sln

# Frontend dev
cd src/ui && npm run dev

# Frontend build
npm run build
```

Requires Docker for integration tests (Testcontainers spins up PostgreSQL).

## License

See repository for license information.
