using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Warp.Core.Interceptors;

namespace Warp.Tests.Fixtures;

public class SqlServerIntegrationFixture : IAsyncLifetime, IDatabaseFixture
{
    private const string DatabaseName = "warp_integration";

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public WarpTestServer TestServer { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await SharedSqlServerContainer.CreateDatabaseAsync(
            DatabaseName,
            enableServiceBroker: true,
            Xunit.TestContext.Current.CancellationToken);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(Xunit.TestContext.Current.CancellationToken);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(Xunit.TestContext.Current.CancellationToken);
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            TablesToIgnore = FixtureHelper.GetServerTablesToIgnore(context),
        });

        TestServer = await WarpTestServer.StartAsync(this);
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

    public async ValueTask DisposeAsync()
    {
        await TestServer.DisposeAsync();
    }
}

[CollectionDefinition]
public class SqlServerIntegrationCollection : ICollectionFixture<SqlServerIntegrationFixture>;
