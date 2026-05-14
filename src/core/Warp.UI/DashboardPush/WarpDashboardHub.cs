using Microsoft.AspNetCore.SignalR;
using Warp.Core.Logging;

namespace Warp.UI.DashboardPush;

/// <summary>
/// Broadcast-only SignalR hub for the Warp dashboard. Clients receive typed events
/// (<c>JobFinalized</c>, <c>MessageEnqueued</c>) carrying empty payloads — events are
/// invalidations, not data. Clients refetch from the REST API to get authoritative state.
/// </summary>
/// <remarks>
/// Auth is enforced upstream by <see cref="UIMiddleware.WarpUIMiddleware"/> — it 401s any
/// unauthenticated request to a path containing <c>/api/</c>, which covers the SignalR
/// negotiate HTTP request and the WebSocket-upgrade HTTP request (both are caught before
/// reaching SignalR). The hub itself only manages connection-count telemetry.
/// </remarks>
public sealed class WarpDashboardHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        WarpTelemetry.DashboardConnectionsActive.Add(1);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
        WarpTelemetry.DashboardConnectionsActive.Add(-1);
    }
}
