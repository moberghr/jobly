using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Warp.Core.Interceptors;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Per-test-class SQL Server fixture. Each test class gets its own database inside the
/// shared SQL Server container, so test classes run fully in parallel up to xunit's
/// MaxParallelThreads. Tests within a class share the database and rely on Respawn between
/// tests for isolation. Service Broker is enabled on every database so notification-transport
/// tests can opt into push without a separate fixture variant.
/// </summary>
public class SqlServerClassFixture : IAsyncLifetime, IDatabaseFixture
{
    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public string ConnectionString => _connectionString;

    public async ValueTask InitializeAsync()
    {
        var databaseName = $"warp_t_{Guid.NewGuid():N}".Substring(0, 16);
        _connectionString = await SharedSqlServerContainer.CreateDatabaseAsync(
            databaseName,
            enableServiceBroker: true,
            Xunit.TestContext.Current.CancellationToken);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(Xunit.TestContext.Current.CancellationToken);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(Xunit.TestContext.Current.CancellationToken);
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
        });
    }

    public async Task ResetAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(Xunit.TestContext.Current.CancellationToken);
        await _respawner.ResetAsync(conn);
    }

    public TestContext CreateContext()
    {
        return new TestContext(new DbContextOptionsBuilder<TestContext>()
            .UseSqlServer(_connectionString)
            .AddInterceptors(new SaveChangesConcurrencyTokenInterceptor())
            .Options);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
