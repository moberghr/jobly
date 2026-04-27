namespace Warp.Core.CircuitBreaker;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CircuitBreakerAttribute : Attribute
{
    public string? Group { get; set; }

    public int Threshold { get; set; }

    public int DurationSeconds { get; set; }

    public int ResetJitterSeconds { get; set; }

    public int GetThreshold(CircuitBreakerOptions options)
    {
        return Threshold > 0 ? Threshold : options.Threshold;
    }

    public TimeSpan GetDuration(CircuitBreakerOptions options)
    {
        return DurationSeconds > 0 ? TimeSpan.FromSeconds(DurationSeconds) : options.Duration;
    }

    public TimeSpan GetResetJitter(CircuitBreakerOptions options)
    {
        return ResetJitterSeconds > 0 ? TimeSpan.FromSeconds(ResetJitterSeconds) : options.ResetJitter;
    }
}
