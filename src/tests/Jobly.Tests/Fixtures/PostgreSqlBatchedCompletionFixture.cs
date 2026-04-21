using Jobly.Core.Interceptors;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Jobly.Tests.Fixtures;

public class PostgreSqlBatchedCompletionFixture : IAsyncLifetime, IDatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:latest")
        .Build();

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public JoblyTestServer TestServer { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(Xunit.TestContext.Current.CancellationToken);
        _connectionString = _container.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(Xunit.TestContext.Current.CancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(Xunit.TestContext.Current.CancellationToken);
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
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
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(Xunit.TestContext.Current.CancellationToken);
        await _respawner.ResetAsync(conn);
    }

    public TestContext CreateContext()
    {
        return new TestContext(new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new SaveChangesConcurrencyTokenInterceptor())
            .Options);
    }

    public async ValueTask DisposeAsync()
    {
        await TestServer.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition]
public class PostgreSqlBatchedCompletionCollection : ICollectionFixture<PostgreSqlBatchedCompletionFixture>;
