namespace Warp.Core.RateLimit;

public interface IRateLimitManager
{
    Task AddOrUpdateLimit(string name, int count, int windowSeconds, CancellationToken ct = default);

    Task<bool> RemoveLimit(string name, CancellationToken ct = default);

    Task<RateLimitInfo?> GetLimit(string name, CancellationToken ct = default);

    Task<IReadOnlyList<RateLimitInfo>> ListLimits(CancellationToken ct = default);
}
