namespace Warp.Tests.Fixtures;

/// <summary>
/// AsyncLocal-backed carrier for a pre-disposal server-state snapshot.
/// <see cref="WarpTestServer.DisposeAsync"/> writes a snapshot here on every disposal;
/// <see cref="FixtureDiagnostics.DumpOnFailureAsync"/> drains it on a failing test and prints
/// the live pre-stop state instead of querying the post-stop DB (where in-flight handlers have
/// already drained and Server / Worker / ServerTask rows have been deleted by graceful
/// shutdown).
/// <para>
/// <b>Opt-in per test.</b> A test that wants the pre-stop dump must call <see cref="Initialize"/>
/// inside its method body, before constructing the <see cref="WarpTestServer"/>:
/// </para>
/// <code>
/// [TimedFact]
/// public async Task Foo()
/// {
///     DiagnosticDumpStorage.Initialize();          // installs the box on THIS async frame
///     await using var server = await WarpTestServer.StartAsync(Fixture);
///     // ... test body
/// }
/// </code>
/// <para>
/// The opt-in is needed because xunit's <c>IAsyncLifetime.InitializeAsync</c> runs on a
/// different <see cref="System.Threading.ExecutionContext"/> than the test method body.
/// AsyncLocal writes from the InitializeAsync frame don't flow into the test method, and a box
/// installed there is invisible to the WarpTestServer disposal that happens inside the test
/// method's <c>await using</c> chain. Calling Initialize from inside the method puts the box
/// in the method's own EC, where the disposal's mutation is observable on the way back out.
/// </para>
/// <para>
/// Implementation: AsyncLocal value writes propagate forward through awaits but not backward.
/// <see cref="Stash"/> from inside <c>WarpTestServer.DisposeAsync</c> can't write the snapshot
/// directly to AsyncLocal — that write would never reach the caller's frame. So the slot
/// holds a mutable box reference; the outer test scope assigns the box via
/// <see cref="Initialize"/>, and inner code mutates the box's field. The reference flows down
/// via AsyncLocal; mutations are observable through shared object identity.
/// </para>
/// <para>
/// When a test hasn't called <see cref="Initialize"/>, <see cref="Stash"/> is a no-op and
/// <see cref="FixtureDiagnostics.DumpOnFailureAsync"/> falls back to its existing
/// post-disposal query path. So the opt-in is strictly additive.
/// </para>
/// </summary>
internal static class DiagnosticDumpStorage
{
    private static readonly AsyncLocal<Box?> Slot = new();

    /// <summary>
    /// Installs a fresh stash box in the current async flow. Call this from the test method
    /// body BEFORE creating a <see cref="WarpTestServer"/>. Subsequent <see cref="Stash"/> calls
    /// from descendant async frames (the server's <c>DisposeAsync</c>) are observable to a
    /// <see cref="Drain"/> call in this same scope.
    /// </summary>
    public static void Initialize() => Slot.Value = new Box();

    public static void Stash(string snapshot)
    {
        if (Slot.Value is { } box)
        {
            box.Snapshot = snapshot;
        }
    }

    public static string? Drain()
    {
        if (Slot.Value is not { } box)
        {
            return null;
        }

        var value = box.Snapshot;
        box.Snapshot = null;

        return value;
    }

    private sealed class Box
    {
        public string? Snapshot { get; set; }
    }
}
