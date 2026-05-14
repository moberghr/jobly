using Medallion.Threading;
using Medallion.Threading.Postgres;
using Npgsql;
using Warp.Core;

namespace Warp.Provider.PostgreSql;

// Medallion's concrete PostgresDistributedSynchronizationProvider uses PostgresAdvisoryLockKey,
// but the IDistributedLockProvider interface method CreateLock(string) wraps a string name
// into that key internally — so we store the reference as the interface type.
internal sealed class PostgresLockProvider : IWarpLockProvider
{
    private readonly IDistributedLockProvider _inner;

    public PostgresLockProvider(string connectionString)
    {
        _inner = new PostgresDistributedSynchronizationProvider(connectionString);
    }

    // Data-source overload: lets callers using NpgsqlDataSource (e.g. Aspire's
    // AddAzureNpgsqlDataSource with Managed Identity / SSL) keep auth and encryption
    // settings centralised — otherwise a raw NpgsqlConnection(connectionString) skips
    // the periodic password provider and SSL config attached to the data source.
    public PostgresLockProvider(NpgsqlDataSource dataSource)
    {
        _inner = new PostgresDistributedSynchronizationProvider(dataSource);
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        var @lock = _inner.CreateLock(name);

        return await @lock.TryAcquireAsync(timeout, ct);
    }
}
