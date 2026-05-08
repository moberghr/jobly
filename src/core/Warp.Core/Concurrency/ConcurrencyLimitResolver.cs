using System.Collections.Concurrent;

namespace Warp.Core.Concurrency;

/// <summary>
/// Resolves the runtime-effective concurrency limit for a key by checking the admin
/// row via <see cref="IConcurrencyLimitManager"/>. Scoped — caches lookups for the
/// lifetime of one job execution scope so the pipeline doesn't re-query the DB if
/// it is invoked more than once for the same key. Cross-scope staleness is
/// intentional: admin updates take effect at the next pickup.
/// </summary>
public sealed class ConcurrencyLimitResolver
{
    private readonly IConcurrencyLimitManager _manager;
    private readonly ConcurrentDictionary<string, int?> _cache = new(StringComparer.Ordinal);

    public ConcurrencyLimitResolver(IConcurrencyLimitManager manager)
    {
        _manager = manager;
    }

    public async Task<int?> GetLimit(string name, CancellationToken ct)
    {
        if (_cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var info = await _manager.GetLimit(name, ct);
        var resolved = info?.Limit;
        _cache[name] = resolved;

        return resolved;
    }
}
