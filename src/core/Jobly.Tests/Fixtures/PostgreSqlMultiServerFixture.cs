using Jobly.Core.Interceptors;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Jobly.Tests.Fixtures;

public class PostgreSqlMultiServerFixture : IAsyncLifetime, IMultiServerDatabaseFixture, IDatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:latest")
        .Build();

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public JoblyTestServer Server1 { get; private set; } = null!;

    public JoblyTestServer Server2 { get; private set; } = null!;

    // IDatabaseFixture.TestServer — not used, but required by the interface for CreateContext reuse
    JoblyTestServer? IDatabaseFixture.TestServer => Server1;

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

        Server1 = await JoblyTestServer.StartAsync(this, config => config.WorkerCount = 3);
        Server2 = await JoblyTestServer.StartAsync(this, config => config.WorkerCount = 3);
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
        await Server2.DisposeAsync();
        await Server1.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition]
public class PostgreSqlMultiServerCollection : ICollectionFixture<PostgreSqlMultiServerFixture>;
