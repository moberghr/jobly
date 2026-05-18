using Warp.Core.BackgroundServices;

namespace Warp.Tests.TestData.BackgroundServices;

/// <summary>
/// Minimal <see cref="WarpBackgroundService"/> that increments a counter on each loop iteration
/// and exits cleanly when the <see cref="CancellationToken"/> is cancelled. Used to verify
/// "yes the service ran" without any blocking.
/// </summary>
public sealed class CountingService : WarpBackgroundService
{
    private readonly CountingServiceState _state;

    public CountingService(CountingServiceState state)
    {
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _state.Count);
            await Task.Yield();
        }
    }
}

public sealed class CountingServiceState
{
    public int Count;
}
