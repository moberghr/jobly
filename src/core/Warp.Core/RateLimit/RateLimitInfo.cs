namespace Warp.Core.RateLimit;

public sealed record RateLimitInfo(string Name, int Count, int WindowSeconds, DateTime UpdatedAt);
