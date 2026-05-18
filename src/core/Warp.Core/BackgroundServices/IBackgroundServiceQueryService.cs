using Microsoft.Extensions.Logging;

namespace Warp.Core.BackgroundServices;

/// <summary>
/// Read-only dashboard queries for the BackgroundServices addon. Presence of this interface in
/// DI is the addon-discovery marker used by <c>GET /api/addons</c> to populate
/// <c>WarpAddonsInfo.Services</c>.
/// </summary>
public interface IBackgroundServiceQueryService
{
    /// <summary>Returns one aggregated row per service name across all servers.</summary>
    Task<IReadOnlyList<BackgroundServiceListItemDto>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Returns full detail for the named service, or <c>null</c> when no
    /// <c>BackgroundServiceDefinition</c> row exists for that name.
    /// </summary>
    Task<BackgroundServiceDetailDto?> GetAsync(string name, CancellationToken ct);

    /// <summary>
    /// Returns the current lease for the named service, or <c>null</c> when no lease row exists
    /// (always the case for per-server services; possible for singletons when no server currently
    /// holds the lease).
    /// </summary>
    Task<BackgroundServiceLeaseDto?> GetLeaseAsync(string name, CancellationToken ct);

    /// <summary>
    /// Returns captured log entries for the named service, newest-first, with optional filters
    /// and cursor-based pagination.
    /// </summary>
    /// <param name="name">Service name to query.</param>
    /// <param name="source">When non-null, only rows with this <see cref="BackgroundServiceLogSource"/> are returned.</param>
    /// <param name="minLevel">When non-null, only rows at or above this level are returned.</param>
    /// <param name="fromId">When non-null, only rows with <c>Id &lt; fromId</c> are returned (exclusive lower bound for paging older entries).</param>
    /// <param name="limit">Maximum rows to return. Callers should cap at 500.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<BackgroundServiceLogDto>> GetLogsAsync(
        string name,
        BackgroundServiceLogSource? source,
        LogLevel? minLevel,
        long? fromId,
        int limit,
        CancellationToken ct);
}
