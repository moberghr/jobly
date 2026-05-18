using Microsoft.Extensions.Logging;

namespace Warp.Core.BackgroundServices;

/// <summary>
/// One captured log entry, returned by <see cref="IBackgroundServiceQueryService.GetLogsAsync"/>.
/// </summary>
public sealed class BackgroundServiceLogDto
{
    public long Id { get; init; }

    public Guid ServerId { get; init; }

    /// <summary>
    /// Human-readable name from the corresponding <c>Server</c> row, resolved through
    /// the <c>BackgroundServiceLog.Server</c> navigation property. Null if the row is
    /// missing (rare — race against ServerCleanup).
    /// </summary>
    public string? ServerName { get; init; }

    public string ServiceName { get; init; } = string.Empty;

    public DateTime Timestamp { get; init; }

    public LogLevel Level { get; init; }

    public BackgroundServiceLogSource Source { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }
}
