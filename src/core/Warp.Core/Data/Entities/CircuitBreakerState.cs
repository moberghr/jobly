using Warp.Core.CircuitBreaker;

namespace Warp.Core.Data.Entities;

public class CircuitBreakerState
{
    public string GroupKey { get; set; } = string.Empty;

    public int FailureCount { get; set; }

    public DateTime? OpenUntil { get; set; }

    public DateTime LastFailureAt { get; set; }

    public CircuitState State { get; set; } = CircuitState.Closed;
}
