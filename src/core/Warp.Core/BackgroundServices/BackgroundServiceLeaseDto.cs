namespace Warp.Core.BackgroundServices;

/// <summary>
/// Current lease holder for a singleton background service, returned by
/// <see cref="IBackgroundServiceQueryService.GetLeaseAsync"/>.
/// </summary>
public sealed class BackgroundServiceLeaseDto
{
    public string ServiceName { get; init; } = string.Empty;

    public Guid HolderServerId { get; init; }

    /// <summary>
    /// Human-readable name from the holder's <c>Server</c> row. Null if the Server row
    /// is missing (rare — race against ServerCleanup).
    /// </summary>
    public string? HolderServerName { get; init; }

    public DateTime LeaseExpiresAt { get; init; }
}
