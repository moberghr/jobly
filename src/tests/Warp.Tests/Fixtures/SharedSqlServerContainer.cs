using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Warp.Tests.Fixtures;

// Process-wide shared MsSqlContainer. xUnit parallelizes across collection fixtures, so
// without this each SqlServer* fixture would boot its own SQL Server (~2GB RSS each). On a
// resource-constrained CI runner that starves every test of CPU/IO and causes flaky time-
// sensitive tests. Each fixture now creates its own uniquely-named database on the single
// shared instance, so DB-level isolation is preserved.
//
// The container is not explicitly disposed — Testcontainers' Ryuk reaper cleans it up when
// the test process exits. This mirrors the original per-fixture behavior under dotnet test.
internal static class SharedSqlServerContainer
{
    // Held so the container object is kept alive for the process lifetime. Testcontainers'
    // Ryuk daemon reaps the Docker container on process exit; we only need to stop this
    // field from being eligible for GC while tests are still running.
#pragma warning disable IDE0052, S4487
    private static MsSqlContainer? _container;
#pragma warning restore IDE0052, S4487
    private static string? _masterConnectionString;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public static async Task<string> CreateDatabaseAsync(string databaseName, bool enableServiceBroker, CancellationToken ct)
    {
        var masterConn = await EnsureContainerAsync(ct);

        await using var conn = new SqlConnection(masterConn);
        await conn.OpenAsync(ct);

        // Database names can't be parameterized in T-SQL DDL; each caller passes a literal
        // from our own fixture code, so no injection risk. Using IF DB_ID IS NULL keeps the
        // call idempotent for repeated test-host invocations while the container persists.
        var brokerStmt = enableServiceBroker
            ? $"ALTER DATABASE [{databaseName}] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;"
            : string.Empty;

        await using var cmd = new SqlCommand(
            $@"IF DB_ID('{databaseName}') IS NULL EXEC('CREATE DATABASE [{databaseName}]');
               {brokerStmt}",
            conn);
        await cmd.ExecuteNonQueryAsync(ct);

        return new SqlConnectionStringBuilder(masterConn) { InitialCatalog = databaseName }.ConnectionString;
    }

    private static async Task<string> EnsureContainerAsync(CancellationToken ct)
    {
        if (_masterConnectionString != null)
        {
            return _masterConnectionString;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_masterConnectionString == null)
            {
                var container = new MsSqlBuilder()
                    .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                    .Build();
                await container.StartAsync(ct);
                _container = container;
                _masterConnectionString = container.GetConnectionString() + ";Encrypt=False;";
            }

            return _masterConnectionString;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
