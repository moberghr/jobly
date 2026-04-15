---
sidebar_position: 1
---

# Getting Started

Jobly is a distributed job processing and message queue library for .NET 10. It provides four patterns:

- **[Messages](./patterns/messages.md)** (`IMessage`) — Pub/sub queue. Multiple handlers per message, each becomes an independent job.
- **[Jobs](./patterns/jobs.md)** (`IJob`) — Orchestrated background work. Single handler, scheduling, retries, continuations, batches.
- **[Requests](./patterns/requests.md)** (`IRequest<TResponse>`) — In-memory request/response. Single handler, no persistence, returns a typed response via `IMediator.Send()`.
- **[Streams](./patterns/requests.md#streams)** (`IStreamRequest<TResponse>`) — In-memory streaming. Single handler, no persistence, returns `IAsyncEnumerable<TResponse>` via `IMediator.CreateStream()`.

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

Jobly automatically adds its interceptors (row locking) and entity configuration (Job, Message, Batch, etc.) when you register Jobly services in the next step. All Jobly tables are placed in the `jobly` schema by default.

:::tip Naming Conventions
Jobly respects EF Core naming conventions. If you use `UseSnakeCaseNamingConvention()`, Jobly's tables and columns will follow your convention automatically.
:::

### 2. Create the database schema

Jobly adds its tables (Job, JobLog, Server, Worker, etc.) to your DbContext model automatically. You just need to create the schema.

**Using EF Core migrations** (recommended for production):

```bash
dotnet ef migrations add AddJobly
dotnet ef database update
```

The migration will include all Jobly tables alongside your own. When you upgrade the Jobly NuGet package and the schema has changed, just add a new migration:

```bash
dotnet ef migrations add UpgradeJobly
dotnet ef database update
```

EF Core detects the model diff automatically — no manual SQL needed.

**Using EnsureCreatedAsync** (quick start / development):

```csharp
var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await context.Database.EnsureCreatedAsync();
```

:::warning EnsureCreated doesn't support upgrades
`EnsureCreatedAsync()` creates the schema from scratch but cannot apply incremental changes. Use EF migrations for production deployments where you need to upgrade Jobly versions without dropping the database.
:::

### 3. Register Jobly

```csharp
// Publisher only — for apps that create jobs but don't process them
builder.Services.AddJobly<AppDbContext>();
builder.Services.AddHandlers(typeof(Program).Assembly);
```

:::tip TimeProvider
Jobly automatically registers `TimeProvider.System` if one is not already registered. Override it in tests to control time.
:::

### 4. Add a worker (optional)

For apps that process jobs, use `AddJoblyWorker` instead (includes `AddJobly` internally):

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    options.WorkerCount = 10;
    options.Queues = ["default", "critical"];
});

// Enable automatic retries with backoff delays
builder.Services.AddJoblyRetry(options =>
{
    options.MaxRetries = 3;
    options.Delays = [15, 60, 300]; // seconds
});
```

### 5. Add the dashboard (optional)

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

### 6. Define handlers

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

### 7. Define a request (optional)

```csharp
public class GetUser : IRequest<UserDto> { public int UserId { get; set; } }

public class GetUserHandler : IRequestHandler<GetUser, UserDto>
{
    public async Task<UserDto> HandleAsync(GetUser request, CancellationToken ct)
    {
        // Query and return
    }
}
```

### 8. Define a stream (optional)

```csharp
public class GetUsers : IStreamRequest<UserDto> { public string Role { get; set; } }

public class GetUsersHandler : IStreamRequestHandler<GetUsers, UserDto>
{
    public async IAsyncEnumerable<UserDto> HandleAsync(GetUsers request, [EnumeratorCancellation] CancellationToken ct)
    {
        // Yield items one at a time
    }
}
```

### 9. Publish & Send

```csharp
public class OrderController : ControllerBase
{
    private readonly IPublisher _publisher;
    private readonly IMediator _mediator;
    private readonly AppDbContext _context;

    public async Task<IActionResult> CreateOrder(Order order)
    {
        _context.Orders.Add(order);

        // Job is created in the same DbContext — committed atomically (outbox pattern)
        await _publisher.Enqueue(new SendEmailRequest { Email = order.Email });

        // Single SaveChangesAsync commits both the order and the job
        await _context.SaveChangesAsync();
        return Ok();
    }

    public async Task<IActionResult> GetUser(int id)
    {
        // In-memory request — no database persistence, immediate response
        var user = await _mediator.Send(new GetUser { UserId = id });
        return Ok(user);
    }
}
```

:::info Transactional Outbox
Jobly uses the [outbox pattern](/docs/features/outbox-pattern) — jobs are written to the same DbContext as your business data and committed in a single `SaveChangesAsync()`. This guarantees atomicity: if the transaction fails, both your data and the jobs roll back. No orphaned jobs, no lost work.
:::
