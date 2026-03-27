# Jobly

A distributed job processing and message queue library for .NET 10. Supports both pub/sub messaging and orchestrated background jobs with a unified pipeline.

## Features

- **Message Queue** — Publish messages with multiple handlers. Each handler runs as an independent, retryable job.
- **Background Jobs** — Schedule and orchestrate jobs with retries, continuations, and batch processing.
- **Pipeline Behaviors** — Middleware chain wraps all handler executions (logging, validation, error handling).
- **Multi-Database** — PostgreSQL and SQL Server with row-level locking for concurrent worker safety.
- **Server Monitoring** — Worker registration, heartbeat tracking, orphaned job recovery.
- **Recurring Jobs** — Cron-based scheduled job execution.
- **Dashboard** — React-based web UI for monitoring jobs and servers.

## Quick Start

### Installation

```csharp
// Register Jobly services
builder.Services.AddJoblyWorker<MyDbContext>(options =>
{
    options.WorkerCount = 5;
    options.PollingInterval = TimeSpan.FromSeconds(1);
});

// Register handlers from your assembly
builder.Services.AddJobHandlers(typeof(Program).Assembly);
```

### Define Messages (Pub/Sub)

```csharp
// A message can have multiple handlers — each runs as an independent job
public class OrderCreated : IMessage
{
    public int OrderId { get; set; }
}

public class SendEmailHandler : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, CancellationToken ct)
    {
        // Send confirmation email
    }
}

public class UpdateInventoryHandler : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, CancellationToken ct)
    {
        // Update stock levels
    }
}

// Publish — both handlers execute independently
await publisher.Publish(new OrderCreated { OrderId = 123 });
```

### Define Jobs (Orchestration)

```csharp
// A job has exactly one handler — supports scheduling, retries, continuations
public class SendMonthlyReport : IJob
{
    public int Month { get; set; }
}

public class SendMonthlyReportHandler : IJobHandler<SendMonthlyReport>
{
    public async Task HandleAsync(SendMonthlyReport message, CancellationToken ct)
    {
        // Generate and send report
    }
}

// Enqueue for immediate execution
await publisher.Enqueue(new SendMonthlyReport { Month = 3 });

// Schedule for later
await publisher.Schedule(new SendMonthlyReport { Month = 3 }, DateTime.UtcNow.AddDays(1));

// With retries
await publisher.Enqueue(new SendMonthlyReport { Month = 3 }, maxRetries: 3);

// Continuation (run after parent completes)
var parentId = await publisher.Enqueue(new PrepareData());
await publisher.Enqueue(new SendMonthlyReport { Month = 3 }, parentJobId: parentId);
```

### Pipeline Behaviors

```csharp
// Wraps ALL handler executions (both IMessage and IJob handlers)
public class LoggingBehavior<T> : IPipelineBehavior<T> where T : class
{
    public async Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken ct)
    {
        Console.WriteLine($"Handling {typeof(T).Name}");
        await next();
        Console.WriteLine($"Handled {typeof(T).Name}");
    }
}
```

## How It Works

### Message Flow (IMessage)
```
Publish(OrderCreated) → Message row (State=Enqueued)
  ↓ Worker routes
  → Job 1 (SendEmailHandler)     → Executes independently
  → Job 2 (UpdateInventoryHandler) → Executes independently
  ↓ All jobs complete
  → Message State = Completed
```

### Job Flow (IJob)
```
Enqueue(SendReport) → Job row (State=Enqueued)
  ↓ Worker executes
  → Handler runs through pipeline
  → Job State = Completed (or Failed → retry)
```

### Concurrency Safety

Workers use database row locking to prevent duplicate processing:
- **PostgreSQL**: `FOR NO KEY UPDATE SKIP LOCKED`
- **SQL Server**: `UPDLOCK READPAST`

Multiple workers can safely poll the same database. Each job/message is processed exactly once.

## Project Structure

```
src/
├── core/
│   ├── Jobly.Core/          # Entities, handlers, publisher, services
│   │   ├── Handlers/        # IJob, IMessage, IJobHandler, IMessageHandler, IPipelineBehavior, JobDispatcher
│   │   ├── Data/Entities/   # Job, Message, JobState, Batch, RecurringJob, Server, Worker
│   │   └── ...
│   ├── Jobly.Worker/        # Background worker service, health manager
│   ├── Jobly.UI/            # Dashboard endpoints and embedded UI
│   └── Jobly.Tests/         # Integration tests (PostgreSQL + SQL Server)
├── tests/
│   ├── Jobly.Test.Shared/   # Shared test handlers and configuration
│   ├── Jobly.TestApp/       # Test web application
│   └── Jobly.TestWorker/    # Test worker service
└── ui/                      # React TypeScript dashboard
```

## Development

```bash
# Build
cd src && dotnet build Jobly.sln

# Test (PostgreSQL only — faster)
dotnet test Jobly.sln --filter "Category!=SqlServer"

# Test (all databases)
dotnet test Jobly.sln
```

Requires Docker for integration tests (Testcontainers spins up PostgreSQL and SQL Server).

## License

See repository for license information.
