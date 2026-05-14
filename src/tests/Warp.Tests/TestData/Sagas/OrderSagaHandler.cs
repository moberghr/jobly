using Warp.Core;
using Warp.Core.Handlers;
using Warp.Core.Sagas;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.TestData.Sagas;

public sealed class BarrierStartMessage : IMessage
{
    [Correlate]
    public string CorrelationKey { get; set; } = string.Empty;
}

[StartsSaga]
public sealed class BarrierStartsMessage : IMessage
{
    [Correlate]
    public string CorrelationKey { get; set; } = string.Empty;
}

/// <summary>
/// Saga handler that blocks inside the handler on a <see cref="BarrierSignal"/>. Used to
/// deterministically force mutex contention: while the first message holds the mutex (and is
/// blocked at the barrier), the second message lands on a different worker, fails to acquire,
/// and gets requeued with the busy outcome.
/// </summary>
public sealed class BarrierSagaHandler(BarrierSignal signal) :
    ISagaHandler<OrderSaga, BarrierStartsMessage>,
    ISagaHandler<OrderSaga, BarrierStartMessage>
{
    public async Task HandleAsync(OrderSaga saga, BarrierStartsMessage message, CancellationToken cancellationToken)
    {
        signal.Running.Release();
        await signal.CanFinish.WaitAsync(cancellationToken);
    }

    public async Task HandleAsync(OrderSaga saga, BarrierStartMessage message, CancellationToken cancellationToken)
    {
        signal.Running.Release();
        await signal.CanFinish.WaitAsync(cancellationToken);
    }
}

[StartsSaga]
public sealed class OrderPlacedPublishingChild : IMessage
{
    [Correlate]
    public string OrderId { get; set; } = string.Empty;
}

public sealed class FollowUpJob : IJob;

public sealed class FollowUpJobHandler : IJobHandler<FollowUpJob>
{
    public Task HandleAsync(FollowUpJob message, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Handler that uses the injected publisher to enqueue a child IJob during saga handling.
/// Validates the S1 invariant: SagaStore.SaveChangesAsync commits the child rows with push
/// notifications, so the worker's outbox doesn't see Unchanged-by-then rows.
/// </summary>
public sealed class OrderPlacedPublishingChildHandler(IPublisher publisher) : ISagaHandler<OrderSaga, OrderPlacedPublishingChild>
{
    public async Task HandleAsync(OrderSaga saga, OrderPlacedPublishingChild message, CancellationToken cancellationToken)
    {
        saga.OrderId = message.OrderId;
        await publisher.Enqueue(new FollowUpJob());

        // Don't call publisher.SaveChangesAsync — the proxy's SagaStore.SaveChangesAsync commits
        // and fires notifications.
    }
}

public sealed class OrderSagaHandler :
    ISagaHandler<OrderSaga, OrderPlaced>,
    ISagaHandler<OrderSaga, PaymentCaptured>,
    ISagaHandler<OrderSaga, InventoryReserved>,
    ISagaHandler<OrderSaga, OrderTimeout>
{
    public Task HandleAsync(OrderSaga saga, OrderPlaced message, CancellationToken cancellationToken)
    {
        saga.OrderId = message.OrderId;
        return Task.CompletedTask;
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
        saga.TimedOut = true;
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
