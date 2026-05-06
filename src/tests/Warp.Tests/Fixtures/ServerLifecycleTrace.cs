using System.Collections.Concurrent;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Test-only in-memory registry that captures <see cref="WarpTestServer"/> lifecycle events
/// with timestamps. Survives server dispose so a failure-path diagnostic dump can answer
/// "did this server's IHost.StartAsync actually return?" without relying on DB rows that
/// <c>WarpServerRegistration.StopAsync</c> would have already deleted.
/// </summary>
internal static class ServerLifecycleTrace
{
    private static readonly ConcurrentQueue<TraceEvent> _events = new();

    public static void Record(Guid serverId, string @event)
        => _events.Enqueue(new TraceEvent(serverId, @event, DateTime.UtcNow));

    public static IReadOnlyList<TraceEvent> Drain()
    {
        var snapshot = _events.ToArray();
        _events.Clear();
        return snapshot;
    }

    public sealed record TraceEvent(Guid ServerId, string Event, DateTime Timestamp);
}
