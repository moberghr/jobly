using Warp.Core;
using Warp.Core.Handlers;
using Warp.Core.Sagas;

namespace Warp.Test.Shared.Handlers.Sagas;

/// <summary>
/// Minimal end-to-end saga showing the three core lifecycle states: start, update, complete.
/// Three messages may arrive in any order; the saga completes when payment is captured AND
/// inventory is reserved. A 5-minute timeout compensates if either never arrives.
/// </summary>
public sealed class OrderSaga : Saga
{
    public string OrderId { get; set; } = string.Empty;

    public bool PaymentCaptured { get; set; }

    public bool InventoryReserved { get; set; }
}

[StartsSaga]
public sealed class OrderPlaced : IMessage
{
    [Correlate]
    public string OrderId { get; set; } = string.Empty;
}

public sealed class PaymentCaptured : IMessage
{
    [Correlate]
    public string OrderId { get; set; } = string.Empty;
}

public sealed class InventoryReserved : IMessage
{
    [Correlate]
    public string OrderId { get; set; } = string.Empty;
}

public sealed class OrderTimeout : ITimeoutMessage
{
    [Correlate]
    public string OrderId { get; set; } = string.Empty;

    // Long enough that sagas stick around in the dashboard for inspection. The seed
    // endpoint produces ORD-S-003 with no payment/inventory follow-up, so this saga will
    // wait the full hour before its timeout fires and completes the saga via the handler.
    // Operators can use "Force complete" from the dashboard to test the compensation path
    // without waiting. Real workflows pick this delay to match their business deadline.
    public TimeSpan Delay => TimeSpan.FromHours(1);
}

public sealed class OrderSagaWorkflow :
    ISagaHandler<OrderSaga, OrderPlaced>,
    ISagaHandler<OrderSaga, PaymentCaptured>,
    ISagaHandler<OrderSaga, InventoryReserved>,
    ISagaHandler<OrderSaga, OrderTimeout>
{
    private readonly IPublisher _publisher;

    public OrderSagaWorkflow(IPublisher publisher) => _publisher = publisher;

    public async Task HandleAsync(OrderSaga saga, OrderPlaced message, CancellationToken cancellationToken)
    {
        saga.OrderId = message.OrderId;

        // Schedule the compensating timeout. If both side flows complete before this fires,
        // the saga will be deleted and the timeout silently dropped (ITimeoutMessage contract).
        await _publisher.Publish(new OrderTimeout { OrderId = message.OrderId });
    }

    public Task HandleAsync(OrderSaga saga, PaymentCaptured message, CancellationToken cancellationToken)
    {
        saga.PaymentCaptured = true;
        MaybeComplete(saga);

        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderSaga saga, InventoryReserved message, CancellationToken cancellationToken)
    {
        saga.InventoryReserved = true;
        MaybeComplete(saga);

        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderSaga saga, OrderTimeout message, CancellationToken cancellationToken)
    {
        // Timeout fired with the saga still alive — at least one of the two confirmations
        // never arrived. In a real implementation you'd publish a compensating action
        // (RefundPayment, ReleaseInventory) before completing.
        saga.MarkCompleted();

        return Task.CompletedTask;
    }

    private static void MaybeComplete(OrderSaga saga)
    {
        if (saga is { PaymentCaptured: true, InventoryReserved: true })
        {
            saga.MarkCompleted();
        }
    }
}
