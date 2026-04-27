using Warp.Core.Data.Entities;

namespace Warp.Core.CircuitBreaker;

public interface ICircuitBreakerStore
{
    Task<CircuitBreakerState?> GetAsync(string groupKey, CancellationToken cancellationToken);

    Task ResetAsync(string groupKey, CancellationToken cancellationToken);

    Task RecordFailureAsync(string groupKey, int threshold, TimeSpan duration, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically transitions a circuit from <see cref="CircuitState.Open"/> to
    /// <see cref="CircuitState.HalfOpen"/> when <c>OpenUntil &lt;= now</c>. Returns
    /// <c>true</c> when this caller wins the probe slot; <c>false</c> if another worker
    /// already took it (or the circuit is no longer open). Callers that win proceed with
    /// a single handler execution as the recovery probe; callers that lose reschedule.
    /// </summary>
    Task<bool> TryBeginProbeAsync(string groupKey, DateTime now, CancellationToken cancellationToken);
}
