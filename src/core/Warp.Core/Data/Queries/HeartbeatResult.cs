namespace Warp.Core.Data.Queries;

/// <summary>
/// Snapshot returned by <see cref="IWarpSqlQueries{TContext}.HeartbeatAsync"/>: the server's
/// <c>paused_at</c> after the UPDATE plus every worker group's pause state for this server.
/// All in one round-trip via CTE+JOIN (PG) or table variable + chained SELECT (SQL Server).
/// </summary>
/// <param name="ServerPausedAt">
/// The server-level pause timestamp post-update. <c>null</c> means not paused.
/// </param>
/// <param name="GroupPaused">
/// Per-worker-group pause state for groups owned by this server. Key is the group's Id,
/// value is <c>true</c> when paused. Empty when the server has no worker groups yet.
/// </param>
public sealed record HeartbeatResult(
    DateTime? ServerPausedAt,
    IReadOnlyDictionary<Guid, bool> GroupPaused);
