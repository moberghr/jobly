using Jobly.Core.Interceptors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;

namespace Jobly.Tests.Fixtures;

public class SqlServerBatchedCompletionFixture : IAsyncLifetime, IDatabaseFixture
{
    private const string DatabaseName = "jobly_batched";

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public JoblyTestServer TestServer { get; private set; } = null!;

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

        TestServer = await JoblyTestServer.StartAsync(this, config =>
        {
            config.UseDispatcher = true;
            config.WorkerCount = 5;
            config.CompletionBatchSize = 10;
            config.CompletionFlushInterval = TimeSpan.FromMilliseconds(50);
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

    public async ValueTask DisposeAsync()
    {
        await TestServer.DisposeAsync();
    }
}

[CollectionDefinition]
public class SqlServerBatchedCompletionCollection : ICollectionFixture<SqlServerBatchedCompletionFixture>;
