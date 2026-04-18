using Jobly.Core.Interceptors;
using Jobly.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Testcontainers.MsSql;

namespace Jobly.Tests.Fixtures;

public class SqlServerBatchedCompletionFixture : IAsyncLifetime, IDatabaseFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public JoblyTestServer TestServer { get; private set; } = null!;

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
        await TestServer.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition]
public class SqlServerBatchedCompletionCollection : ICollectionFixture<SqlServerBatchedCompletionFixture>;
