using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Warp.Core.Interceptors;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Per-test-class PostgreSQL fixture. Each test class gets its own database inside the
/// shared PostgreSQL container, so test classes run fully in parallel up to xunit's
/// MaxParallelThreads. Tests within a class share the database and rely on Respawn between
/// tests for isolation.
/// </summary>
public class PostgreSqlClassFixture : IAsyncLifetime, IDatabaseFixture
{
    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public string ConnectionString => _connectionString;

    public async ValueTask InitializeAsync()
    {
        TestLifecycleTrace.Record("PostgreSqlClassFixture.InitializeAsync starting");
        var databaseName = $"warp_t_{Guid.NewGuid():N}";

        TestLifecycleTrace.Record("SharedPostgreSqlContainer.CreateDatabaseAsync starting");
        var rawConnectionString = await SharedPostgreSqlContainer.CreateDatabaseAsync(
            databaseName,
            Xunit.TestContext.Current.CancellationToken);
        TestLifecycleTrace.Record("SharedPostgreSqlContainer.CreateDatabaseAsync returned");

        // Bake MaxPoolSize into the per-fixture connection string. EF builds an independent
        // NpgsqlDataSource per options instance (fixture's CreateContext, WarpTestServer's
        // worker registration, etc.) and each gets its own pool. Without a per-pool cap the
        // aggregate easily exceeds the testcontainer's max_connections (500) under parallel
        // test-class load — a small cap (50) keeps every pool bounded so even 4 parallel
        // classes × 2 pools × 50 stays well under the cap.
        _connectionString = new NpgsqlConnectionStringBuilder(rawConnectionString)
        {
            MaxPoolSize = 50,
        }.ConnectionString;

        TestLifecycleTrace.Record("EnsureCreatedAsync starting");
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(Xunit.TestContext.Current.CancellationToken);
        TestLifecycleTrace.Record("EnsureCreatedAsync returned");

        TestLifecycleTrace.Record("Respawner.CreateAsync starting");
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(Xunit.TestContext.Current.CancellationToken);
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
        });
        TestLifecycleTrace.Record("Respawner.CreateAsync returned");
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
