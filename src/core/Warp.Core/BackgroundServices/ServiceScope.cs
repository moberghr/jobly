namespace Warp.Core.BackgroundServices;

/// <summary>
/// Controls how many instances of a <c>WarpBackgroundService</c> run across the cluster.
/// </summary>
public enum ServiceScope
{
    /// <summary>
    /// One independent instance per server. No coordination required.
    /// </summary>
    PerServer = 1,

    /// <summary>
    /// Exactly one instance across the entire cluster at any time. Warp uses a lease-based
    /// election to determine the holder; other servers wait in <c>Waiting</c> status and
    /// take over on graceful shutdown or lease expiry (~30s).
    /// </summary>
    Singleton = 2,
}
