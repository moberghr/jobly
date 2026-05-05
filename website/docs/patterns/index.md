---
sidebar_position: 1
---

# Patterns

Warp provides four patterns for dispatching work. All share a unified pipeline and type hierarchy.

| Pattern | Interface | Persistence | Handlers | Response |
|---------|-----------|-------------|----------|----------|
| [Messages](./messages.md) | `IMessage` | Database | Multiple | None (Unit) |
| [Jobs](./jobs.md) | `IJob` | Database | Single | None (Unit) |
| [Requests](./requests.md) | `IRequest<T>` | In-memory | Single | Typed `T` |
| [Streams](./requests.md#streams) | `IStreamRequest<T>` | In-memory | Single | `IAsyncEnumerable<T>` |

## Type Hierarchy

All types implement `IRequest<TResponse>`:

```csharp
public interface IRequest<out TResponse>;                                        // Base
public interface IJob : IRequest<Unit>;                                          // Persistent, single handler
public interface IMessage : IRequest<Unit>;                                      // Persistent, multiple handlers
// IRequest<TResponse> used directly                                            // In-memory, returns TResponse
public interface IStreamRequest<out TResponse> : IRequest<IAsyncEnumerable<TResponse>>; // In-memory, streams TResponse
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

For jobs and messages, `TResponse` is `Unit`. For requests, it's your custom response type. For streams, it's `IAsyncEnumerable<T>`. Logger output from pipeline behaviors appears in the job detail "Handler Output" section.

## Publish Pipeline Behaviors

`IPublishPipelineBehavior<T>` intercepts the *publish* side — before a job is written to the database. Use it to attach cross-cutting metadata (tenant ID, correlation ID, user context) to every job automatically:

```csharp
public class TenantBehavior<T> : IPublishPipelineBehavior<T>
{
    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        context.Metadata["tenant"] = _tenantContext.CurrentTenantId;
        return next();
    }
}
```

Register as an open generic:

```csharp
builder.Services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(TenantBehavior<>));
```

Metadata set here is inherited by all child jobs spawned during execution. See [Metadata](/docs/features/metadata) for the full model.

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
