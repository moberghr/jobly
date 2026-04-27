using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Warp.Core.Interceptors;

namespace Warp.Tests.Fixtures;

public class SqlServerMultiServerFixture : IAsyncLifetime, IMultiServerDatabaseFixture, IDatabaseFixture
{
    private const string DatabaseName = "warp_multi";

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public WarpTestServer Server1 { get; private set; } = null!;

    public WarpTestServer Server2 { get; private set; } = null!;

    // IDatabaseFixture.TestServer — not used, but required by the interface for CreateContext reuse
    WarpTestServer? IDatabaseFixture.TestServer => Server1;

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
            TablesToIgnore = FixtureHelper.GetServerTablesToIgnore(context),
        });

        Server1 = await WarpTestServer.StartAsync(this, config => config.WorkerCount = 3);
        Server2 = await WarpTestServer.StartAsync(this, config => config.WorkerCount = 3);
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
        await Server2.DisposeAsync();
        await Server1.DisposeAsync();
    }
}

[CollectionDefinition]
public class SqlServerMultiServerCollection : ICollectionFixture<SqlServerMultiServerFixture>;
