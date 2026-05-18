using Microsoft.Extensions.Logging;

namespace Warp.Core.BackgroundServices;

/// <summary>
/// Minimal intake surface exposed by <c>BackgroundServiceLogCollector</c> to the logger and
/// lifecycle logger. Keeps those components in <c>Warp.Core</c> while the full collector
/// implementation (with EF Core flush path) lives in <c>Warp.Worker</c>.
/// </summary>
public interface IBackgroundServiceLogCollector
{
    /// <summary>
    /// Enqueues a log entry after applying the configured level filter and rate cap.
    /// Thread-safe; may be called from any thread.
    /// </summary>
    void Enqueue(BackgroundServiceLogSource source, LogLevel level, string message, Exception? exception);
}
