using Jobly.Core.Data.Entities;

namespace Jobly.Core.CircuitBreaker;

public interface ICircuitBreakerStore
{
    Task<CircuitBreakerState?> GetAsync(string groupKey, CancellationToken cancellationToken);

    Task ResetAsync(string groupKey, CancellationToken cancellationToken);

    Task RecordFailureAsync(string groupKey, int threshold, TimeSpan duration, DateTime now, CancellationToken cancellationToken);
}
