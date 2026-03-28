using Jobly.Core.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Jobly.Tests.Fixtures;

public class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:latest")
        .Build();

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public string ConnectionString => _connectionString;

    public PostgresRowLockInterceptor Interceptor { get; } = new();

    public SaveChangesConcurrencyTokenInterceptor ConcurrencyInterceptor { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        await

                // Create schema once
                using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await

                // Init Respawn — skip Statistic table (has seed data)
                using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            TablesToIgnore = [new Respawn.Graph.Table("Statistic")],
            DbAdapter = DbAdapter.Postgres,
        });
    }

    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
        await

                // Reset statistics back to 0
                using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(@"UPDATE ""Statistic"" SET value = 0");
    }

    public TestContext CreateContext()
    {
        return new TestContext(new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(Interceptor, ConcurrencyInterceptor)
            .Options);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("PostgreSql")]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>;
