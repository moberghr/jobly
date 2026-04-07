---
sidebar_position: 1
---

# Patterns

Jobly provides three patterns for dispatching work. All share a unified pipeline and type hierarchy.

| Pattern | Interface | Persistence | Handlers | Response |
|---------|-----------|-------------|----------|----------|
| [Messages](./messages.md) | `IMessage` | Database | Multiple | None (Unit) |
| [Jobs](./jobs.md) | `IJob` | Database | Single | None (Unit) |
| [Requests](./requests.md) | `IRequest<T>` | In-memory | Single | Typed `T` |

## Type Hierarchy

All types implement `IRequest<TResponse>`:

```csharp
public interface IRequest<out TResponse>;     // Base
public interface IJob : IRequest<Unit>;        // Persistent, single handler
public interface IMessage : IRequest<Unit>;    // Persistent, multiple handlers
// IRequest<TResponse> used directly           // In-memory, returns TResponse
```

## Pipeline Behaviors

A unified pipeline wraps all handler invocations — jobs, messages, and requests:

```csharp
public class LoggingBehavior<T, TResponse> : IPipelineBehavior<T, TResponse>
    where T : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(T request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _logger.LogInformation("Starting {Type}", typeof(T).Name);
        var result = await next();
        _logger.LogInformation("Completed {Type}", typeof(T).Name);
        return result;
    }
}
```

Register as an open generic:

```csharp
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

You can also target specific types:

```csharp
// Only for GetUser requests
public class CacheBehavior : IPipelineBehavior<GetUser, UserDto> { ... }

// Only for jobs (any IJob)
public class RetryBehavior<T> : IPipelineBehavior<T, Unit> where T : IJob { ... }
```

For jobs and messages, `TResponse` is `Unit`. For requests, it's your custom response type. Logger output from pipeline behaviors appears in the job detail "Handler Output" section.
