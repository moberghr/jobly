namespace Warp.Core.Data.Entities;

public class RateLimitOverride
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }

    public int WindowSeconds { get; set; }

    public DateTime UpdatedAt { get; set; }
}
