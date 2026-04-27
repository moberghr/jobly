namespace Warp.Core.Retry;

public class RetryOptions
{
    public int MaxRetries { get; set; }

    public int[] Delays { get; set; } = [15, 60, 300];

    /// <summary>
    /// Multiplicative jitter factor applied to each retry delay. Clamped to [0, 1].
    /// Formula: delay * (1 + JitterFactor * rand(-1, 1)). Default 0.0 (no jitter).
    /// </summary>
    public double JitterFactor { get; set; }
}
