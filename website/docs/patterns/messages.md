---
sidebar_position: 1
---

# Messages

Messages implement `IMessage` and support **multiple handlers**. When published, the worker discovers all registered handlers and creates a separate job for each — pub/sub style.

## Define a message

```csharp
public class OrderPlaced : IMessage
{
    public int OrderId { get; set; }
}
```

## Define handlers

```csharp
public class SendConfirmationEmail : IMessageHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced message, CancellationToken ct)
    {
        // Send email
    }
}

public class NotifyWarehouse : IMessageHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced message, CancellationToken ct)
    {
        // Notify warehouse
    }
}
```

## Publish

```csharp
await publisher.Publish(new OrderPlaced { OrderId = 123 });
await context.SaveChangesAsync(); // Persisted atomically with your data
```

## How it works

1. `Publish()` creates a `Job` entity with `Kind = Message` in the database
2. `MessageRouter` discovers all registered `IMessageHandler<T>` implementations
3. For each handler, a child `Job` is created with `Kind = Job` and the handler pre-assigned
4. Workers pick up each child job and execute it independently
5. When all children complete, the parent message transitions to `Completed` or `Failed`

Messages are visible in the dashboard under the Messages tab.
