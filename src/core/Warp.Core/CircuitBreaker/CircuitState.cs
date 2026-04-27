namespace Warp.Core.CircuitBreaker;

public enum CircuitState
{
    Closed = 0,
    Open = 1,
    HalfOpen = 2,
}
