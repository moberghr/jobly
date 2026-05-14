namespace Warp.Core.Data.Entities;

public class RateLimitBucket
{
    public string Name { get; set; } = string.Empty;

    public DateTime WindowStartUtc { get; set; }

    public int CurrentCount { get; set; }

    public string? TimestampsJson { get; set; }

    public DateTime UpdatedAt { get; set; }
}
