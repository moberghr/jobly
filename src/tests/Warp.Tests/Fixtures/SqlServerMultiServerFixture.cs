using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Warp.Core.Interceptors;

namespace Warp.Tests.Fixtures;

public class SqlServerMultiServerFixture : IAsyncLifetime, IDatabaseFixture
{
    private const string DatabaseName = "warp_multi";

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await SharedSqlServerContainer.CreateDatabaseAsync(
            DatabaseName,
            enableServiceBroker: false,
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

[CollectionDefinition]
public class SqlServerMultiServerCollection : ICollectionFixture<SqlServerMultiServerFixture>;
