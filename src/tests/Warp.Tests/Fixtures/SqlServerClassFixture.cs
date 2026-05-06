using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Warp.Core.Interceptors;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Per-test-class SQL Server fixture. Each test class gets its own database inside the
/// shared SQL Server container, so test classes run fully in parallel up to xunit's
/// MaxParallelThreads. Tests within a class share the database and rely on Respawn between
/// tests for isolation.
/// <para>
/// Service Broker is NOT enabled by default — it adds ~100–200ms per fixture-init via
/// <c>ALTER DATABASE … SET ENABLE_BROKER</c>. Tests that exercise the SQL Server
/// notification transport (push) must use <see cref="SqlServerPushClassFixture"/> instead.
/// </para>
/// </summary>
public class SqlServerClassFixture : IAsyncLifetime, IDatabaseFixture
{
    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public string ConnectionString => _connectionString;

    protected virtual bool EnableServiceBroker => false;

    public async ValueTask InitializeAsync()
    {
        var databaseName = $"warp_t_{Guid.NewGuid():N}";
        _connectionString = await SharedSqlServerContainer.CreateDatabaseAsync(
            databaseName,
            EnableServiceBroker,
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
            .AddInterceptors(new SaveChangesConcurrencyTokenInterceptor())
            .Options);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// SQL Server fixture variant with Service Broker enabled. Use for test classes that exercise
/// <c>SqlServerNotificationTransport</c> directly or call <c>UseDatabasePush()</c> in their
/// server config — without Broker, the transport's listener can't subscribe.
/// </summary>
public sealed class SqlServerPushClassFixture : SqlServerClassFixture
{
    protected override bool EnableServiceBroker => true;
}
