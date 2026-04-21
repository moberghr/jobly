namespace Jobly.Worker;

/// <summary>
/// Thread-safe singleton that holds the current pause state for this server.
/// Updated by Heartbeat every 3s, read by workers on each poll iteration.
/// Uses an immutable snapshot swapped atomically to avoid torn reads.
/// </summary>
public class PauseStateHolder
{
    private sealed record PauseSnapshot(bool ServerPaused, Dictionary<Guid, bool> GroupPaused);

    private volatile PauseSnapshot _state = new(false, []);

    public bool IsPaused(Guid? workerGroupId)
    {
        var snapshot = _state;
        return snapshot.ServerPaused
            || (workerGroupId.HasValue
                && snapshot.GroupPaused.TryGetValue(workerGroupId.Value, out var paused)
                && paused);
    }

    public void Update(bool serverPaused, Dictionary<Guid, bool> groupPaused)
    {
        _state = new PauseSnapshot(serverPaused, groupPaused);
    }
}
