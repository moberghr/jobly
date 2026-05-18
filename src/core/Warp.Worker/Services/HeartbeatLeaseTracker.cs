namespace Warp.Worker.Services;

/// <summary>
/// Singleton state holder for the <c>Heartbeat</c> task's per-tick lease tracking.
/// Stores the set of service names whose <c>BackgroundServiceLease</c> was renewed on the
/// previous beat, so <c>Heartbeat</c> (which is <c>Scoped</c> and gets a fresh instance each
/// tick) can compute <c>lost = previousHeld - renewedThisBeat</c> across ticks.
/// </summary>
/// <remarks>
/// All public methods are thread-safe via an internal lock. Only one Heartbeat instance runs
/// per tick (no lock is required for the task itself), but defensive locking keeps the class
/// honest if the registration ever changes.
/// </remarks>
public sealed class HeartbeatLeaseTracker
{
    private readonly Lock _gate = new();

    // Two sets that swap each tick: no per-iteration allocation on the hot path.
    private HashSet<string> _previousHeld = [];
    private HashSet<string> _currentHeld = [];

    /// <summary>
    /// Records the service names renewed this beat, computes the names lost since the
    /// previous beat (previousHeld - renewedThisBeat), then swaps the buffers for the
    /// next tick. Returns the list of lost names (may be empty).
    /// </summary>
    public IReadOnlyList<string> SwapAndComputeLost(IReadOnlyList<string> renewedThisBeat)
    {
        lock (_gate)
        {
            _currentHeld.Clear();
            foreach (var name in renewedThisBeat)
            {
                _currentHeld.Add(name);
            }

            var lost = _previousHeld
                .Where(name => !_currentHeld.Contains(name))
                .ToList();

            (_previousHeld, _currentHeld) = (_currentHeld, _previousHeld);

            return lost;
        }
    }

    /// <summary>
    /// Test seam: pre-populates <c>_previousHeld</c> so tests can drive the lost-lease
    /// detection path deterministically without waiting for a real heartbeat tick to
    /// populate the set. Must not be called from production code.
    /// </summary>
    internal void SeedForTest(IEnumerable<string> heldNames)
    {
        lock (_gate)
        {
            _previousHeld.Clear();
            foreach (var name in heldNames)
            {
                _previousHeld.Add(name);
            }
        }
    }
}
