using Medallion.Threading.SqlServer;
using Warp.Core;

namespace Warp.Provider.SqlServer;

internal sealed class SqlServerSemaphoreProvider : IWarpSemaphoreProvider
{
    private readonly SqlDistributedSynchronizationProvider _inner;

    public SqlServerSemaphoreProvider(string connectionString)
    {
        _inner = new SqlDistributedSynchronizationProvider(connectionString);
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, int maxCount, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);

        return await _inner.CreateSemaphore(name, maxCount).TryAcquireAsync(timeout, ct);
    }
}
