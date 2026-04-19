using Jobly.Core.Interceptors;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Jobly.Tests.Fixtures;

public class PostgreSqlFixture : IAsyncLifetime, IDatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:latest")
        .Build();

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public string ConnectionString => _connectionString;

    public JoblyTestServer? TestServer => null;

    internal PostgresRowLockInterceptor Interceptor { get; } = new();

    internal SaveChangesConcurrencyTokenInterceptor ConcurrencyInterceptor { get; } = new();

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
            .AddInterceptors(Interceptor, ConcurrencyInterceptor)
            .Options);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>;
