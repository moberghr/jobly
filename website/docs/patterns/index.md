---
sidebar_position: 1
---

# Patterns

Jobly provides four patterns for dispatching work.

| Pattern | Interface | Persistence | Handlers | Response |
|---------|-----------|-------------|----------|----------|
| [Messages](./messages.md) | `IMessage` | Database | Multiple | None (Unit) |
| [Jobs](./jobs.md) | `IJob` | Database | Single | None (Unit) |
| [Requests](./requests.md) | `IRequest<T>` | In-memory | Single | Typed `T` |
| [Streams](./requests.md#streams) | `IStreamRequest<T>` | In-memory | Single | `IAsyncEnumerable<T>` |

## Type Hierarchy

Jobs and messages implement `IRequest<TResponse>`. Streams have a separate base interface:

```csharp
public interface IRequest<out TResponse>;          // Base — jobs, messages, and requests implement this
public interface IJob : IRequest<Unit>;             // Persistent, single handler
public interface IMessage : IRequest<Unit>;         // Persistent, multiple handlers
// IRequest<TResponse> used directly                // In-memory, returns TResponse
public interface IStreamRequest<out TResponse>;     // Separate — in-memory streaming, returns IAsyncEnumerable<TResponse>
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

Stream requests use a separate pipeline — `IStreamPipelineBehavior<TRequest, TResponse>`:

```csharp
public class StreamLoggingBehavior<T, TResponse> : IStreamPipelineBehavior<T, TResponse>
    where T : IStreamRequest<TResponse>
{
    public async IAsyncEnumerable<TResponse> HandleAsync(T request, StreamHandlerDelegate<T, TResponse> next, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("Starting stream {Type}", typeof(T).Name);
        await foreach (var item in next(request, ct).WithCancellation(ct))
        {
            yield return item;
        }

        _logger.LogInformation("Completed stream {Type}", typeof(T).Name);
    }
}
```
