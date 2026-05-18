using Microsoft.Extensions.Logging;
using Warp.Core.BackgroundServices;

namespace Warp.Core.Data.Entities;

/// <summary>
/// Captured log entry for a background service instance. Sourced from the service's own
/// <c>ILogger&lt;T&gt;</c> calls (<c>Source = User</c>) and from lifecycle events emitted by
/// the supervisor (<c>Source = Lifecycle</c>). Deleted by retention sweep or by cascade
/// when the owning <c>BackgroundServiceInstance</c> row is removed.
/// </summary>
public class BackgroundServiceLog
{
    public long Id { get; set; }

    public Guid ServerId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public LogLevel Level { get; set; }

    public BackgroundServiceLogSource Source { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? ExceptionType { get; set; }

    public string? ExceptionMessage { get; set; }

    public Server? Server { get; set; }
}
