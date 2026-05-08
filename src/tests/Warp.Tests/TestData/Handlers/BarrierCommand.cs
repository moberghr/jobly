using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class BarrierRequest : IJob;

public class BarrierCommand : IJobHandler<BarrierRequest>
{
    private readonly BarrierSignal _signal;

    public BarrierCommand(BarrierSignal signal)
    {
        _signal = signal;
    }

    public async Task HandleAsync(BarrierRequest message, CancellationToken cancellationToken)
    {
        _signal.Running.Release();
        await _signal.CanFinish.WaitAsync(cancellationToken);
    }
}

public class BarrierMessage : IMessage;

public class BarrierMessageHandler : IMessageHandler<BarrierMessage>
{
    private readonly BarrierSignal _signal;

    public BarrierMessageHandler(BarrierSignal signal)
    {
        _signal = signal;
    }

    public async Task HandleAsync(BarrierMessage message, CancellationToken cancellationToken)
    {
        _signal.Running.Release();
        await _signal.CanFinish.WaitAsync(cancellationToken);
    }
}

public class BarrierSignal
{
    public SemaphoreSlim Running { get; } = new(0);

    public SemaphoreSlim CanFinish { get; } = new(0);
}
