using Warp.Core.BackgroundServices;

namespace Warp.Tests.TestData.BackgroundServices;

/// <summary>
/// <see cref="WarpBackgroundService"/> designed for the healthy-reset integration test.
/// First call: blocks on <see cref="HealthyResetServiceState.RunningGate"/> until released,
/// then exits without throwing so the supervisor treats it as a graceful-return fault.
/// Second call (after the first "fault"): throws <see cref="InvalidOperationException"/> so
/// the supervisor records a fault and triggers the healthy-reset logic.
/// </summary>
public sealed class HealthyResetService : WarpBackgroundService
{
    private readonly HealthyResetServiceState _state;

    public HealthyResetService(HealthyResetServiceState state)
    {
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var attempt = Interlocked.Increment(ref _state.Attempts);

        if (attempt == 1)
        {
            // First attempt: signal running, then block until the test releases the gate.
            // Returning without cancellation is treated as a fault by the supervisor.
            _state.RunningGate.Release();
            await _state.CanAdvanceTime.WaitAsync(ct);

            return;
        }

        // Second and subsequent attempts: fault immediately so RestartCount increments,
        // then the healthy-reset path should reset it back to zero.
        _state.FaultedGate.Release();
        throw new InvalidOperationException("HealthyResetService deliberate second-attempt fault");
    }
}

public sealed class HealthyResetServiceState
{
    public int Attempts;

    /// <summary>Released by the service on the first attempt to signal it is running.</summary>
    public SemaphoreSlim RunningGate { get; } = new(0);

    /// <summary>Released by the test to unblock the first attempt (triggering graceful-return fault).</summary>
    public SemaphoreSlim CanAdvanceTime { get; } = new(0);

    /// <summary>Released by the service on the second attempt before throwing.</summary>
    public SemaphoreSlim FaultedGate { get; } = new(0);
}
