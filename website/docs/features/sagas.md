---
sidebar_position: 7
---

# Sagas

Sagas are **long-lived, stateful conversations** — workflows where unsolicited messages arrive over time, all referencing the same business identifier, and each message's handler needs access to whatever state previous messages have already established.

The canonical example: an order placement spans hours. `OrderPlaced` arrives, then later `PaymentCaptured`, then `InventoryReserved`. Once both payment and inventory are confirmed, ship. You need somewhere to remember "I've seen payment but not inventory yet" between message arrivals — that's the saga.

## Setup

```csharp
builder.Services.AddWarpWorker<AppDbContext>(opt =>
{
    opt.UsePostgreSql();
    opt.AddSagas();
});

// Register handler classes (one call per class):
builder.Services.AddSagaHandler<OrderWorkflow>();
```

`AddSagas()` contributes the `Saga` entity to your DbContext (run `dotnet ef migrations add AddWarpSagas` to pick it up) and registers the infrastructure. `AddSagaHandler<>()` reflects over the handler's implemented `ISagaHandler<TSaga, TMessage>` interfaces and registers a `SagaHandlerProxy<TSaga, TMessage>` as `IMessageHandler<TMessage>` for each.

## Defining a saga

A saga has three parts: the **state class** (subclass `Saga`), the **correlated messages** (each carries a `[Correlate]` property; one or more are marked `[StartsSaga]`), and the **handler class** (implements `ISagaHandler<TSaga, TMessage>` for each message type that touches the saga).

```csharp
// State — subclass Warp.Core.Sagas.Saga
public class OrderSaga : Saga
{
    public string OrderId { get; set; } = "";
    public bool PaymentCaptured { get; set; }
    public bool InventoryReserved { get; set; }
}

// Messages — must carry exactly one [Correlate] string property
[StartsSaga]
public class OrderPlaced : IMessage
{
    [Correlate] public string OrderId { get; set; } = "";
}

public class PaymentCaptured : IMessage
{
    [Correlate] public string OrderId { get; set; } = "";
}

public class InventoryReserved : IMessage
{
    [Correlate] public string OrderId { get; set; } = "";
}

// Handler — one class implements all the ISagaHandler<,> interfaces for this saga
public class OrderWorkflow(IPublisher publisher) :
    ISagaHandler<OrderSaga, OrderPlaced>,
    ISagaHandler<OrderSaga, PaymentCaptured>,
    ISagaHandler<OrderSaga, InventoryReserved>
{
    public Task HandleAsync(OrderSaga saga, OrderPlaced m, CancellationToken ct)
    {
        saga.OrderId = m.OrderId;
        return Task.CompletedTask;
    }

    public async Task HandleAsync(OrderSaga saga, PaymentCaptured m, CancellationToken ct)
    {
        saga.PaymentCaptured = true;
        await MaybeShipAsync(saga, ct);
    }

    public async Task HandleAsync(OrderSaga saga, InventoryReserved m, CancellationToken ct)
    {
        saga.InventoryReserved = true;
        await MaybeShipAsync(saga, ct);
    }

    private async Task MaybeShipAsync(OrderSaga saga, CancellationToken ct)
    {
        if (saga is { PaymentCaptured: true, InventoryReserved: true })
        {
            await publisher.Enqueue(new ShipOrder(saga.OrderId));
            saga.MarkCompleted();
        }
    }
}
```

:::warning Do not call `publisher.SaveChangesAsync` inside a saga handler

The saga proxy commits everything in one transaction after `HandleAsync` returns — your saga state changes, any jobs/messages you enqueued via the publisher, and (on completion) the row deletion. Calling `publisher.SaveChangesAsync(ct)` inside the handler commits the publisher's pending rows **early**, which means by the time the proxy runs its own `SaveChanges` those rows are `Unchanged` and **push notifications are silently dropped** — the children fall back to polling. Let the proxy save for you.

:::

Publish messages normally:

```csharp
await publisher.Publish(new OrderPlaced { OrderId = "O-123" });
await publisher.SaveChangesAsync();
```

The saga proxy:

1. Reads the `[Correlate]` property → `OrderId = "O-123"`.
2. Acquires a distributed mutex on `warp:saga:OrderSaga:O-123` (timeout 0).
3. Loads the saga row (`Type='OrderSaga' AND CorrelationKey='O-123'`).
4. If no row exists and the message has `[StartsSaga]`, creates a fresh `OrderSaga`. Otherwise calls `NotFoundAsync` (see below).
5. Invokes your `HandleAsync(saga, message, ct)`.
6. Persists state (insert, update, or — if `MarkCompleted()` was called — delete).
7. Releases the mutex.

## Completion

Inside any handler, call `saga.MarkCompleted()` to signal the saga is done. The proxy deletes the row in the same `SaveChanges` as the handler's final update. The correlation key becomes immediately reusable for a fresh saga.

```csharp
saga.MarkCompleted();
```

There is no `IsCompleted` setter — completion is a one-way transition initiated by the handler.

## Timeouts (`ITimeoutMessage`)

A common saga pattern: "if no payment arrives within 10 minutes, cancel the order." Warp ships this as `ITimeoutMessage` — a marker on the message class that tells `Publisher.Publish` to **schedule the message** at `now + Delay` instead of delivering it immediately.

```csharp
public class OrderTimeout : ITimeoutMessage
{
    [Correlate]
    public string OrderId { get; set; } = "";

    public TimeSpan Delay => TimeSpan.FromMinutes(10);
}

public class OrderWorkflow(IPublisher publisher) :
    ISagaHandler<OrderSaga, OrderPlaced>,
    ISagaHandler<OrderSaga, OrderTimeout>
{
    public async Task HandleAsync(OrderSaga saga, OrderPlaced m, CancellationToken ct)
    {
        saga.OrderId = m.OrderId;
        // Schedule the timeout. ScheduleTime = now + 10 minutes; row goes to State.Scheduled.
        await publisher.Publish(new OrderTimeout { OrderId = m.OrderId });
    }

    public Task HandleAsync(OrderSaga saga, OrderTimeout t, CancellationToken ct)
    {
        // The timeout fired and the saga is still alive — payment never arrived. Compensate.
        saga.MarkCompleted();
        return Task.CompletedTask;
    }
}
```

`ScheduledJobActivation` (the server task that flips `Scheduled` → `Enqueued` when `ScheduleTime` elapses) drives the activation. The cadence is `WarpWorkerConfiguration.ScheduledActivationInterval` (default 5s) — that's the worst-case latency between the timeout's nominal fire time and the message actually being routed.

### Timeout-after-completion is silent

If the saga completes (and is deleted) **before** the timeout fires, the timeout arrives at a saga that no longer exists. The proxy detects this case — `TMessage` implements `ITimeoutMessage` — and silently transitions the routed handler-job to `Deleted` rather than `Failed`. The timeout was a safety net; the saga did its job; firing the timeout is moot.

This means you don't need to "cancel" pending timeouts when a saga completes successfully. The timeout will fire, find the saga gone, and quietly self-clean. (Wolverine's pattern; we follow it.)

### `Delay <= TimeSpan.Zero` is immediate

Publishing an `ITimeoutMessage` with `Delay = TimeSpan.Zero` (or negative) falls through to the normal "deliver immediately" path. Useful when the delay is computed dynamically and may evaluate to zero — you don't need a branch for that case.

## Missing-saga handling (`NotFoundAsync`)

When a non-`[StartsSaga]` message arrives for an unknown correlation key, the proxy:

1. Pre-sets `IJobContext.Outcome = { State = Failed, LogMessage = "No saga for ..." }`.
2. Calls your `ISagaHandler.NotFoundAsync(message, context, ct)`.

The default `NotFoundAsync` implementation is a no-op, so the pre-set `Failed` outcome stands — the missing-saga case surfaces as a job failure in the dashboard. You can override to:

- **Silently ignore** the message: set `context.Outcome = new JobOutcome { State = State.Deleted, LogMessage = "skipped" }`.
- **Log and skip**: same as above but with a richer log message.
- **Emit a compensation** message via your injected `IPublisher`.

```csharp
public Task NotFoundAsync(PaymentCaptured msg, IJobContext context, CancellationToken ct)
{
    context.Outcome = new JobOutcome
    {
        State = State.Deleted,
        LogMessage = $"Payment for unknown order {msg.OrderId} — ignored",
    };
    return Task.CompletedTask;
}
```

## Serialization across concurrent messages

The mutex acquired on `warp:saga:{SagaType.FullName}:{CorrelationKey}` is the cross-process serialization mechanism. If two messages for the same saga land on different workers simultaneously, one acquires the mutex and proceeds; the other is **requeued** with `State = Enqueued` and the next polling tick picks it up.

Optimistic concurrency on the `Version` column is a defense-in-depth backstop. A `DbUpdateConcurrencyException` during save (which the mutex makes effectively impossible, but is theoretically possible if the lock provider hiccups) also requeues the message.

## When to use a saga vs. another primitive

| You need… | Use |
|---|---|
| Run B after A finishes | Job continuation (`Enqueue(b, parentJobId: a.Id)`) |
| Run N jobs in parallel, finalize when all done | `BatchPublisher.StartNew` |
| Run a fixed deterministic sequence A → B → C | Job continuations (`parentJobId` chain). Sagas add mutex + state-row overhead with no benefit when you control all the timing. |
| Run something on a cron schedule | `AddOrUpdateRecurringJob` |
| Handle one event independently | Plain `IMessageHandler<T>` |
| **Track state across unsolicited future events sharing a business key** | **Saga** |

## Typed correlation keys

`[Correlate]` works on `string`, `Guid`, `int`, and `long` properties. The framework canonicalizes the value to a single string format under the hood, so the storage column stays one `varchar(200)` and unique indexes work without per-type tables.

```csharp
public class OrderPlaced : IMessage
{
    [Correlate]
    public Guid OrderId { get; set; }   // Guid, not string
}
```

On the saga side, inherit from `Saga<TKey>` to get a typed `Key` property:

```csharp
public class OrderSaga : Saga<Guid>
{
    public bool PaymentCaptured { get; set; }
}

// In a handler:
public Task HandleAsync(OrderSaga saga, OrderPlaced m, CancellationToken ct)
{
    var orderId = saga.Key;   // Guid, no parsing
    return Task.CompletedTask;
}
```

Canonical formats:

| Type | Stored as | Notes |
|---|---|---|
| `string` | identity | what you wrote |
| `Guid` | `ToString("N")` (32 hex chars) | no dashes — same format on both ends |
| `int` | invariant decimal | works on European-locale machines |
| `long` | invariant decimal | same |

Any other type (`decimal`, `DateTime`, strong-typed wrappers) throws `SagaConfigurationException` at first use. Wrap them in `Guid`/`int`/`long`/`string` if you need them.

**Default-valued typed keys throw at publish.** `Guid.Empty`, `int 0`, and `long 0L` are rejected — a default-valued `[Correlate]` property almost always indicates the field was forgotten or model-bound from absent input, and silently joining the all-zeros singleton saga is a worst-case correlation bleed (especially in multi-tenant deployments). If you genuinely need 0 as a sentinel, use a non-zero offset (e.g. always +1, or `long.MinValue + 1`) or use `string` keys.

## Operational notes

:::warning Correlation keys appear in logs — don't use PII

The correlation key is interpolated into `JobLog.Message` rows whenever the proxy logs a saga outcome (`"Requeued — saga 'X' busy for '{key}'"`, `"No saga of type 'X' for correlation key '{key}'"`, etc.). It also appears in the saga's distributed-lock name and in OpenTelemetry tags. **Use opaque identifiers (Guid, integer) as correlation keys, not email addresses, account numbers, or other PII.** If you must correlate on PII, hash it before publishing the message.

:::

### Saga state must be JSON-round-trippable

The saga subclass is serialized with default `System.Text.Json.JsonSerializer` options. Keep state shapes simple — primitives, strings, nested POCOs with the same constraints, nullable variants. Avoid: polymorphic property types without `[JsonDerivedType]`, `DateTime` properties that rely on a particular `Kind` (always use UTC), custom converters that aren't registered at the framework level. Custom serializer options are not configurable in v1.

### Saga property renames are tolerated; removals lose data

`SagaStore.Load` deserializes with `UnmappedMemberHandling = Skip`, so a saga subclass that renames or removes a property loads existing rows with default values for the absent field. This avoids breaking every persisted saga on a property rename, but it means **removed properties silently lose data**. If you rename a saga property, add a [data migration step][1] or accept that in-flight sagas will reset their renamed-away field to its default until they receive their next message. Same applies to property removals — known trade-off, documented here so it doesn't surprise you in production.

[1]: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/

### Saga class renames invalidate in-flight sagas

The `Saga` table's `Type` column stores `typeof(TSaga).FullName`. If you rename `OrderSaga` to `OrderWorkflow`, every persisted row's `Type` still points at the old name. New messages with the renamed type get `null` from `Load` and either start a fresh saga (if `[StartsSaga]`) or fail. Drain in-flight sagas before renaming, or write a one-shot `UPDATE warp.saga SET type = '<new>' WHERE type = '<old>'` as part of the deploy.

## Dashboard pages

When `opt.AddSagas()` is registered, the dashboard exposes:

- `/warp/sagas` — list of live sagas with type filter, correlation-key search, header stats (live count, started today, types in use)
- `/warp/sagas/{id}` — saga detail: pretty-printed `StateJson`, activity log of all jobs that touched this saga, force-complete button (type-to-confirm modal)

The nav entry is hidden when `AddSagas()` is not registered (the API endpoints return 404 and the layout probes once on mount).

The activity log query is backed by an extension table `SagaJobLink` (one row per saga-handler invocation, cleaned up cascade-style on completion) so the lookup is an indexed `(SagaId, CreatedAt)` scan rather than a JSON-parse over the Job table.

## Saga expiration

**Sagas never expire automatically.** The only exit paths are:

1. **Handler-initiated** — `saga.MarkCompleted()` deletes the `SagaState` row and its `SagaJobLink` rows in the same `SaveChanges`.
2. **Operator-initiated** — the dashboard's "Force complete" button on the saga detail page (`DELETE /api/sagas/{id}`).
3. **Direct DB intervention** — manual cleanup.

There is no TTL, no `ExpireAt`, no background cleanup task for sagas. The `SagaState` table grows monotonically until a saga is explicitly completed. Long-lived sagas (subscriptions, hour-grade approval flows) are legitimate.

For "auto-abandon if no progress in N days," schedule a self-cleanup `ITimeoutMessage`:

```csharp
public class OrderSaga : Saga
{
    public string OrderId { get; set; } = "";
    public bool PaymentCaptured { get; set; }
}

public class OrderAbandonedTimeout : ITimeoutMessage
{
    [Correlate] public string OrderId { get; set; } = "";
    public TimeSpan Delay => TimeSpan.FromDays(30);
}

// In a handler:
//   await publisher.Publish(new OrderAbandonedTimeout { OrderId = saga.OrderId });
// The timeout fires 30 days later. If the saga is still alive, the timeout handler calls
// MarkCompleted. If the saga already completed normally, the proxy's "timeout missing-saga
// silent return" branch quietly drops the timeout.
```

This pairs with `ITimeoutMessage`'s missing-saga silent-drop behavior — completed sagas don't leave noise behind.

## Limitations (v1)

- **No state-machine DSL.** All branching happens inside your `HandleAsync` methods. Wolverine's `Initially`/`During`/`Finally`-style DSL is not part of v1.
- **Single correlation key per message.** `[Correlate]` on multiple properties throws at runtime. Alternate-key correlation (NServiceBus-style) is not in v1.
- **Sagas attach only to `IMessage`, not `IJob`.** Jobs are single-handler, single-shot — they don't fit the multi-message-correlation shape. Wrap a job in a message if you need both.
- **No audit history after completion.** Sagas are deleted on `MarkCompleted()`, so the dashboard's `CompletedToday` stat is always 0 and there's no historical row to query. The `warp.sagas.completed` OTel counter records the count but not individual saga IDs. If you need post-completion traceability, write your own audit event from the handler before calling `MarkCompleted()`.
- **No automatic expiry.** Sagas live until `MarkCompleted()` or operator `ForceComplete`. A misconfigured saga that never completes accumulates forever. Schedule a self-cleanup `ITimeoutMessage` if you need a hard ceiling.

## Telemetry

Counters (emitted via `WarpTelemetry.Meter`):

- `warp.sagas.started` — tagged `saga_type`
- `warp.sagas.completed` — tagged `saga_type`
- `warp.sagas.requeued` — tagged `saga_type` and `reason` (`busy` | `version`)

Wire these into your OpenTelemetry pipeline via the standard `Warp` meter source.

## Migration

`AddSagas()` adds a single `Saga` entity to your DbContext model. After enabling the addon, run:

```bash
dotnet ef migrations add AddWarpSagas
dotnet ef database update
```

The migration adds one table (`warp.saga` by default) with a primary key on `id`, a unique index on `(type, correlation_key)`, and a `Guid` row version. No changes to existing Warp tables.
