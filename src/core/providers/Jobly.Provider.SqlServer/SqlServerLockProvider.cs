using Jobly.Core;
using Medallion.Threading;
using Medallion.Threading.SqlServer;

namespace Jobly.Provider.SqlServer;

internal sealed class SqlServerLockProvider : IJoblyLockProvider
{
    private readonly IDistributedLockProvider _inner;

    public SqlServerLockProvider(string connectionString)
    {
        _inner = new SqlDistributedSynchronizationProvider(connectionString);
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        var @lock = _inner.CreateLock(name);

        return await @lock.TryAcquireAsync(timeout, ct);
    }
}
