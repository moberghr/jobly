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

public class BarrierSignal
{
    public SemaphoreSlim Running { get; } = new(0);

    public SemaphoreSlim CanFinish { get; } = new(0);
}
