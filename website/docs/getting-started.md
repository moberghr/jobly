---
sidebar_position: 1
---

# Getting Started

Jobly is a distributed job processing and message queue library for .NET 10. It provides two patterns:

- **Messages** (`IMessage`) — Pub/sub queue. Multiple handlers per message, each becomes an independent job.
- **Jobs** (`IJob`) — Orchestrated background work. Single handler, scheduling, retries, continuations, batches.

## Installation

```bash
dotnet add package Jobly.Core    # Publishing (your app)
dotnet add package Jobly.Worker  # Worker service
dotnet add package Jobly.UI      # Dashboard
```

## Setup

### 1. Register Jobly in your DbContext

```csharp
public class AppDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddOutboxStateEntity(); // Adds Job, Message, Batch, etc.
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseNpgsql(connectionString)
               .AddJoblyInterceptors(); // Required for row locking
    }
}
```

### 2. Register services

```csharp
// In your app (publisher side)
builder.Services.AddJobly<AppDbContext>();
builder.Services.AddJobHandlers(typeof(Program).Assembly);

// In your worker
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    options.WorkerCount = 10;
    options.Queues = new[] { "default", "critical" };
});

// Dashboard
app.UseJoblyUI();
```

### 3. Define handlers

```csharp
public class SendEmailRequest : IJob { public string Email { get; set; } }

public class SendEmailHandler : IJobHandler<SendEmailRequest>
{
    public async Task HandleAsync(SendEmailRequest message, CancellationToken ct)
    {
        // Send the email
    }
}
```

### 4. Publish

```csharp
public class OrderController : ControllerBase
{
    private readonly IPublisher _publisher;
    private readonly AppDbContext _context;

    public async Task<IActionResult> CreateOrder(Order order)
    {
        _context.Orders.Add(order);

        // Job is created in the same transaction (outbox pattern)
        await _publisher.Enqueue(new SendEmailRequest { Email = order.Email });

        await _context.SaveChangesAsync(); // Both saved atomically
        return Ok();
    }
}
```

:::info DbContext must be Scoped
Jobly requires your DbContext to be registered as **Scoped** (the EF Core default). This ensures the publisher and your application code share the same DbContext instance within a scope, so jobs are committed atomically with your business data.
:::
