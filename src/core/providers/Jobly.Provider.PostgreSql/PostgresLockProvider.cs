using Jobly.Core;
using Medallion.Threading;
using Medallion.Threading.Postgres;

namespace Jobly.Provider.PostgreSql;

// Medallion's concrete PostgresDistributedSynchronizationProvider uses PostgresAdvisoryLockKey,
// but the IDistributedLockProvider interface method CreateLock(string) wraps a string name
// into that key internally — so we store the reference as the interface type.
internal sealed class PostgresLockProvider : IJoblyLockProvider
{
    private readonly IDistributedLockProvider _inner;

    public PostgresLockProvider(string connectionString)
    {
        _inner = new PostgresDistributedSynchronizationProvider(connectionString);
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        var @lock = _inner.CreateLock(name);

        return await @lock.TryAcquireAsync(timeout, ct);
    }
}
