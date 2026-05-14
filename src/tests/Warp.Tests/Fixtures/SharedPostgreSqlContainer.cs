using Npgsql;
using Testcontainers.PostgreSql;

namespace Warp.Tests.Fixtures;

// Process-wide shared PostgreSqlContainer. See SharedSqlServerContainer for rationale.
// PG containers are lighter than SQL Server (~200MB vs ~2GB), but consolidating for
// symmetry also halves container boot time (1×N seconds instead of 4×N seconds).
internal static class SharedPostgreSqlContainer
{
#pragma warning disable IDE0052, S4487
    private static PostgreSqlContainer? _container;
#pragma warning restore IDE0052, S4487
    private static string? _adminConnectionString;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public static async Task<string> CreateDatabaseAsync(string databaseName, CancellationToken ct)
    {
        var adminConn = await EnsureContainerAsync(ct);

        await using var conn = new NpgsqlConnection(adminConn);
        await conn.OpenAsync(ct);

        // Identifier comes from our own fixture code, never user input. PG's CREATE DATABASE
        // can't be parameterized, so we check existence via pg_database and skip if present
        // — idempotent for repeated test-host invocations while the container persists.
        await using (var checkCmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name",
            conn))
        {
            checkCmd.Parameters.AddWithValue("name", databaseName);
            var exists = await checkCmd.ExecuteScalarAsync(ct);
            if (exists == null)
            {
                await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", conn);
                await createCmd.ExecuteNonQueryAsync(ct);
            }
        }

        return new NpgsqlConnectionStringBuilder(adminConn) { Database = databaseName }.ConnectionString;
    }

    // Sibling to CreateDatabaseAsync — releases the per-fixture database when the class
    // fixture is disposed. DROP DATABASE WITH (FORCE) (PG 13+) terminates any sessions still
    // connected to it, which is what reclaims connections back to the testcontainer's
    // max_connections budget. Without this every fixture leaks its full MaxPoolSize=50 quota
    // server-side until the test process exits — see PostgreSqlClassFixture.DisposeAsync.
    public static async Task DropDatabaseAsync(string databaseName, CancellationToken ct)
    {
        var adminConn = await EnsureContainerAsync(ct);

        await using var conn = new NpgsqlConnection(adminConn);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
            conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> EnsureContainerAsync(CancellationToken ct)
    {
        if (_adminConnectionString != null)
        {
            return _adminConnectionString;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_adminConnectionString == null)
            {
                var container = new PostgreSqlBuilder()
                    .WithImage("postgres:latest")
                    .WithCommand("-c", "max_connections=500")
                    .Build();
                await container.StartAsync(ct);
                _container = container;
                _adminConnectionString = container.GetConnectionString();
            }

            return _adminConnectionString;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
