---
sidebar_position: 4
---

# Job Tracing

Jobly automatically tracks the flow of jobs across handlers. When a job handler spawns new jobs, they share a `TraceId`, making the full execution chain visible in the dashboard.

## How It Works

Every job gets two trace fields:

- **TraceId** — All related jobs share this ID. The first job in a flow creates it (`TraceId = own ID`). All spawned jobs inherit it.
- **SpawnedByJobId** — Direct "who created me" link.

This happens automatically via `AsyncLocal` context. When a handler calls `publisher.Enqueue()` or `batchPublisher.StartNew()`, the new jobs inherit the trace.

## Example

```csharp
public class ProcessOrderHandler : IJobHandler<ProcessOrderRequest>
{
    private readonly IBatchPublisher _batchPublisher;

    public async Task HandleAsync(ProcessOrderRequest message, CancellationToken ct)
    {
        // These jobs automatically inherit the trace from ProcessOrderRequest
        var shipItems = items.Select(i => new ShipItemRequest { ItemId = i.Id }).ToList();
        var batchId = await _batchPublisher.StartNew(shipItems);

        // Continuation also inherits the trace
        await _batchPublisher.ContinueBatchWith(
            new List<SendInvoiceRequest> { new() { OrderId = message.OrderId } },
            batchId);
    }
}
```

The dashboard shows the full trace:

![Job detail with trace](/img/screenshots/03-job-detail-trace.png)

The "Trace (9 jobs)" card shows all jobs spawned from this ProcessOrderRequest: 6 ShipItemRequests and 2 PublishInvoiceRequests. Clicking any job navigates to its detail, which shows the same trace from that job's perspective.

## Message-Routed Jobs

When a message is routed to multiple handlers, all resulting jobs share a `TraceId`:

```csharp
await publisher.Publish(new OrderNotification()); // Routes to EmailHandler + SlackHandler
// Both jobs get the same TraceId
```
