using Warp.Core.BackgroundServices;

namespace Warp.Tests.TestData.BackgroundServices;

/// <summary>
/// <see cref="WarpBackgroundService"/> that signals arrival on entry and then blocks on a
/// barrier semaphore. Tests can deterministically pin the service inside <c>ExecuteAsync</c>
/// and assert state before releasing via <see cref="BackgroundServiceBarrierSignal.CanFinish"/>.
/// Uses the <see cref="BarrierSignal"/> pattern (§4.7 — N=2 for concurrency, single service
/// for lifecycle tests).
/// </summary>
public sealed class BarrierPinnedService : WarpBackgroundService
{
    private readonly BackgroundServiceBarrierSignal _signal;

    public BarrierPinnedService(BackgroundServiceBarrierSignal signal)
    {
        _signal = signal;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _signal.Running.Release();
        await _signal.CanFinish.WaitAsync(ct);
    }
}

/// <summary>
/// Barrier signal for <see cref="BarrierPinnedService"/>. Register as a singleton in the
/// test server's DI so the test and the service share the same instance.
/// </summary>
public sealed class BackgroundServiceBarrierSignal
{
    public SemaphoreSlim Running { get; } = new(0);

    public SemaphoreSlim CanFinish { get; } = new(0);
}
