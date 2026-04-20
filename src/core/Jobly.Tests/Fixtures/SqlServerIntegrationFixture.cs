using Jobly.Core.Interceptors;
using Jobly.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Testcontainers.MsSql;

namespace Jobly.Tests.Fixtures;

public class SqlServerIntegrationFixture : IAsyncLifetime, IDatabaseFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public JoblyTestServer TestServer { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(Xunit.TestContext.Current.CancellationToken);
        var masterConnString = _container.GetConnectionString() + ";Encrypt=False;";

        // Use a dedicated database (not master) so Service Broker can be enabled — system
        // databases can't be altered by name/CURRENT. Mirrors SqlServerFixture setup so
        // SqlServerNotificationTransport integration tests have broker available.
        await using (var bootstrapConn = new SqlConnection(masterConnString))
        {
            await bootstrapConn.OpenAsync(Xunit.TestContext.Current.CancellationToken);
            await using var bootstrapCmd = new SqlCommand(
                @"IF DB_ID('jobly_tests') IS NULL CREATE DATABASE [jobly_tests];
                  ALTER DATABASE [jobly_tests] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;",
                bootstrapConn);
            await bootstrapCmd.ExecuteNonQueryAsync(Xunit.TestContext.Current.CancellationToken);
        }

        var builder = new SqlConnectionStringBuilder(masterConnString) { InitialCatalog = "jobly_tests" };
        _connectionString = builder.ConnectionString;

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(Xunit.TestContext.Current.CancellationToken);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(Xunit.TestContext.Current.CancellationToken);
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            TablesToIgnore = FixtureHelper.GetServerTablesToIgnore(context),
        });

        TestServer = await JoblyTestServer.StartAsync(this);
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
            .AddInterceptors(new SqlServerRowLockInterceptor(), new SaveChangesConcurrencyTokenInterceptor())
            .Options);
    }

    public async ValueTask DisposeAsync()
    {
        await TestServer.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition]
public class SqlServerIntegrationCollection : ICollectionFixture<SqlServerIntegrationFixture>;
