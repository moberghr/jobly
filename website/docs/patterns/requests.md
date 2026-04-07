---
sidebar_position: 3
---

# Requests

Requests implement `IRequest<TResponse>` and have a **single handler** that returns a typed response. Unlike jobs and messages, requests are **not persisted to the database** — they execute immediately in-process via `IMediator.Send()`.

Use requests for queries, commands that need a response, or any synchronous in-process work that benefits from the pipeline.

## Define a request

```csharp
public class GetUser : IRequest<UserDto>
{
    public int UserId { get; set; }
}

public class GetUserHandler : IRequestHandler<GetUser, UserDto>
{
    private readonly AppDbContext _db;

    public GetUserHandler(AppDbContext db) => _db = db;

    public async Task<UserDto> HandleAsync(GetUser request, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync(request.UserId, ct);
        return new UserDto { Id = user.Id, Name = user.Name };
    }
}
```

## Send

Inject `IMediator` and call `Send()`:

```csharp
public class UserController : ControllerBase
{
    private readonly IMediator _mediator;

    public async Task<IActionResult> GetUser(int id)
    {
        var user = await _mediator.Send(new GetUser { UserId = id });
        return Ok(user);
    }
}
```

## How it works

1. `Send()` resolves `IRequestHandler<TRequest, TResponse>` from DI
2. Wraps the handler in the `IPipelineBehavior<TRequest, TResponse>` chain
3. Executes in-process, returns `TResponse` directly
4. No database, no Job entity, no worker involved

## Key differences from Jobs/Messages

| | Jobs/Messages | Requests |
|---|---|---|
| Storage | Persisted to database | In-memory only |
| Execution | Background worker | Immediate, in-process |
| Response | None (Unit) | Typed TResponse |
| Retries | Automatic | None (caller handles errors) |
| Dashboard | Visible in UI | Not visible |
| Interface | `IPublisher.Enqueue` / `Publish` | `IMediator.Send` |

## Pipeline Behaviors

All three patterns (jobs, messages, requests) run through a unified pipeline. Implement `IPipelineBehavior<TRequest, TResponse>` to add cross-cutting concerns like logging, validation, or timing:

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        _logger.LogInformation("Handling {RequestType}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("Handled {RequestType}", typeof(TRequest).Name);
        return response;
    }
}
```

### Registration

Auto-scan an assembly for all pipeline behavior implementations:

```csharp
builder.Services.AddPipelineBehaviors(typeof(Program).Assembly);
```

Or register manually:

```csharp
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

Behaviors execute in registration order, outermost first. The handler is the innermost call.
