using Warp.Core.BackgroundServices;

namespace Warp.Tests.TestData.BackgroundServices;

/// <summary>
/// <see cref="WarpBackgroundService"/> that throws <see cref="InvalidOperationException"/> on
/// the first call and runs cleanly (blocking on a barrier) on the second. Used to verify that
/// the supervisor's fault-and-restart path works correctly.
/// </summary>
public sealed class ThrowingService : WarpBackgroundService
{
    private readonly ThrowingServiceState _state;

    public ThrowingService(ThrowingServiceState state)
    {
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var attempt = Interlocked.Increment(ref _state.Attempts);

        if (attempt == 1)
        {
            throw new InvalidOperationException("Deliberate first-attempt fault from ThrowingService");
        }

        // Second and subsequent attempts: signal that we reached user code, then block.
        _state.Recovered.Release();
        await _state.CanFinish.WaitAsync(ct);
    }
}

public sealed class ThrowingServiceState
{
    public int Attempts;

    public SemaphoreSlim Recovered { get; } = new(0);

    public SemaphoreSlim CanFinish { get; } = new(0);
}
