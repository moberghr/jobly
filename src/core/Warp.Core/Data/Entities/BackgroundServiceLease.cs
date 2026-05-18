namespace Warp.Core.Data.Entities;

/// <summary>
/// Singleton-coordination lease. One row per singleton service name. Created or updated on
/// acquisition; deleted on graceful release or by <c>ServerCleanup</c> for the dead holder.
/// TTL is 30 seconds by default; renewed on every <c>Heartbeat</c> tick.
/// </summary>
public class BackgroundServiceLease
{
    public string ServiceName { get; set; } = string.Empty;

    public Guid HolderServerId { get; set; }

    public DateTime LeaseExpiresAt { get; set; }

    public Server? HolderServer { get; set; }
}
