namespace Warp.Core.Concurrency;

public interface IConcurrencyLimitManager
{
    Task AddOrUpdateLimit(string name, int limit, CancellationToken ct = default);

    Task<bool> RemoveLimit(string name, CancellationToken ct = default);

    Task<ConcurrencyLimitInfo?> GetLimit(string name, CancellationToken ct = default);

    Task<IReadOnlyList<ConcurrencyLimitInfo>> ListLimits(CancellationToken ct = default);
}
