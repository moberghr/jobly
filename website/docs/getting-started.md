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

### 1. Register your DbContext

Register your DbContext as usual — no special configuration needed:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
```

Jobly automatically adds its interceptors (row locking) and entity configuration (Job, Message, Batch, etc.) when you register Jobly services in the next step.

### 2. Register Jobly

```csharp
// Publisher only — for apps that create jobs but don't process them
builder.Services.AddJobly<AppDbContext>();
builder.Services.AddJobHandlers(typeof(Program).Assembly);
```

### 3. Add a worker (optional)

For apps that process jobs, use `AddJoblyWorker` instead (includes `AddJobly` internally):

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    options.WorkerCount = 10;
    options.Queues = ["default", "critical"];
});
```

### 4. Add the dashboard (optional)

```csharp
app.UseJoblyUI(); // Serves at /jobly
```

To protect the dashboard with authentication:

```csharp
// Dashboard with auth (optional)
app.UseJoblyUI(options =>
{
    options.Authorization = new MyAuthFilter();
    options.UnauthorizedRedirectUrl = "/login";
});
```

:::tip TimeProvider
Jobly automatically registers a `TimeProvider` in the DI container if one is not already registered. You do not need to add it yourself.
:::

### 5. Define handlers

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

### 6. Publish

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
