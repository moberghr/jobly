using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Warp.Core.Interceptors;

namespace Warp.Tests.Fixtures;

public class PostgreSqlBatchedCompletionFixture : IAsyncLifetime, IDatabaseFixture
{
    private const string DatabaseName = "warp_batched";

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await SharedPostgreSqlContainer.CreateDatabaseAsync(
            DatabaseName,
            Xunit.TestContext.Current.CancellationToken);

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
            .AddInterceptors(new SaveChangesConcurrencyTokenInterceptor())
            .Options);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[CollectionDefinition]
public class PostgreSqlBatchedCompletionCollection : ICollectionFixture<PostgreSqlBatchedCompletionFixture>;
