namespace Warp.Core.CircuitBreaker;

public class CircuitBreakerOptions
{
    public int Threshold { get; set; } = 3;

    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan ResetJitter { get; set; } = TimeSpan.FromSeconds(10);
}
