using System.Collections.Concurrent;

namespace Warp.Core.RateLimit;

/// <summary>
/// Resolves the runtime-effective rate-limit settings for a key by checking the admin
/// row via <see cref="IRateLimitManager"/>. Scoped — caches lookups for the lifetime of
/// one job execution scope. Cross-scope staleness is intentional: admin updates take
/// effect at the next pickup. Mirrors <see cref="Warp.Core.Concurrency.ConcurrencyLimitResolver"/>.
/// </summary>
public sealed class RateLimitResolver
{
    private readonly IRateLimitManager _manager;
    private readonly ConcurrentDictionary<string, RateLimitInfo?> _cache = new(StringComparer.Ordinal);

    public RateLimitResolver(IRateLimitManager manager)
    {
        _manager = manager;
    }

    public async Task<RateLimitInfo?> GetOverride(string name, CancellationToken ct)
    {
        if (_cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var info = await _manager.GetLimit(name, ct);
        _cache[name] = info;

        return info;
    }
}
