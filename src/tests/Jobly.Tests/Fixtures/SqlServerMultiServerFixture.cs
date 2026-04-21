using Jobly.Core.Interceptors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;

namespace Jobly.Tests.Fixtures;

public class SqlServerMultiServerFixture : IAsyncLifetime, IMultiServerDatabaseFixture, IDatabaseFixture
{
    private const string DatabaseName = "jobly_multi";

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public JoblyTestServer Server1 { get; private set; } = null!;

    public JoblyTestServer Server2 { get; private set; } = null!;

    // IDatabaseFixture.TestServer — not used, but required by the interface for CreateContext reuse
    JoblyTestServer? IDatabaseFixture.TestServer => Server1;

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

        Server1 = await JoblyTestServer.StartAsync(this, config => config.WorkerCount = 3);
        Server2 = await JoblyTestServer.StartAsync(this, config => config.WorkerCount = 3);
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
