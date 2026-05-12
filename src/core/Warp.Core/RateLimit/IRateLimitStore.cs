using Warp.Core.Data.Entities;

namespace Warp.Core.RateLimit;

public interface IRateLimitStore
{
    Task<RateLimitBucket?> GetAsync(string name, CancellationToken ct);

    Task UpsertAsync(string name, DateTime windowStartUtc, int currentCount, string? timestampsJson, DateTime updatedAt, CancellationToken ct);
}
