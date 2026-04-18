using Jobly.Core.Interceptors;
using Jobly.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Testcontainers.MsSql;

namespace Jobly.Tests.Fixtures;

public class SqlServerMultiServerFixture : IAsyncLifetime, IMultiServerDatabaseFixture, IDatabaseFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public JoblyTestServer Server1 { get; private set; } = null!;

    public JoblyTestServer Server2 { get; private set; } = null!;

    // IDatabaseFixture.TestServer — not used, but required by the interface for CreateContext reuse
    JoblyTestServer? IDatabaseFixture.TestServer => Server1;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString() + ";Encrypt=False;";

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
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
        await conn.OpenAsync();
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
        await Server2.DisposeAsync();
        await Server1.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition]
public class SqlServerMultiServerCollection : ICollectionFixture<SqlServerMultiServerFixture>;
