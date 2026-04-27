using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Warp.Core.Interceptors;

namespace Warp.Tests.Fixtures;

public class SqlServerFixture : IAsyncLifetime, IDatabaseFixture
{
    private const string DatabaseName = "warp_default";

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public string ConnectionString => _connectionString;

    public WarpTestServer? TestServer => null;

    internal SaveChangesConcurrencyTokenInterceptor ConcurrencyInterceptor { get; } = new();

    public async ValueTask InitializeAsync()
    {
        // Service Broker must be enabled so SqlServerNotificationTransport tests can listen.
        _connectionString = await SharedSqlServerContainer.CreateDatabaseAsync(
            DatabaseName,
            enableServiceBroker: true,
            Xunit.TestContext.Current.CancellationToken);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(Xunit.TestContext.Current.CancellationToken);
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(Xunit.TestContext.Current.CancellationToken);
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
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
            .AddInterceptors(ConcurrencyInterceptor)
            .Options);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[CollectionDefinition]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
