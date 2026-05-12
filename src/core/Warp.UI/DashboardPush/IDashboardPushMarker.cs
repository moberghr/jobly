namespace Warp.UI.DashboardPush;

/// <summary>
/// Marker service registered iff <c>opt.AddDashboardPush()</c> was called. Resolved
/// from DI by the probe endpoint (returns 404 when absent — hide-on-404 pattern matching
/// <c>/api/concurrency</c>) and by <c>UseWarpUI</c> to decide whether to <c>MapHub</c>.
/// </summary>
public interface IDashboardPushMarker;

public sealed class DashboardPushMarker : IDashboardPushMarker;
