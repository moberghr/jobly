using Warp.Core.BackgroundServices;

namespace Warp.Core.Data.Entities;

/// <summary>
/// One row per (server, service) pair. Inserted when the host starts; deleted on graceful
/// shutdown. <c>ServerCleanup</c> removes stale rows for servers that missed their heartbeat.
/// </summary>
public class BackgroundServiceInstance
{
    public Guid ServerId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public ServiceScope DeclaredScope { get; set; }

    public BackgroundServiceStatus Status { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime LastHeartbeatAt { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastErrorAt { get; set; }

    public int RestartCount { get; set; }

    public Server? Server { get; set; }
}
