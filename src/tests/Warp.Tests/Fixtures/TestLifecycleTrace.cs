using System.Collections.Concurrent;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Captures pre-host setup events (class-fixture InitializeAsync, IntegrationTestBase
/// InitializeAsync, Fixture.ResetAsync, WarpTestServer.StartAsync prep) so the failure-path
/// diagnostic dump can show where the budget went before <see cref="ServerLifecycleTrace"/>'s
/// IHost.StartAsync entries pick up. Same drain-on-dump semantics as ServerLifecycleTrace.
/// </summary>
internal static class TestLifecycleTrace
{
    private static readonly ConcurrentQueue<TraceEvent> _events = new();

    public static void Record(string @event)
    {
        var testName = Xunit.TestContext.Current?.Test?.TestDisplayName ?? "<no-test>";
        _events.Enqueue(new TraceEvent(testName, @event, DateTime.UtcNow));
    }

    public static IReadOnlyList<TraceEvent> Drain()
    {
        var snapshot = _events.ToArray();
        _events.Clear();
        return snapshot;
    }

    public sealed record TraceEvent(string TestName, string Event, DateTime Timestamp);
}
