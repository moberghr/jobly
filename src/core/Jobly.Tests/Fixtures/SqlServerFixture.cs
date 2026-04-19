using Jobly.Core.Interceptors;
using Jobly.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Testcontainers.MsSql;

namespace Jobly.Tests.Fixtures;

public class SqlServerFixture : IAsyncLifetime, IDatabaseFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public JoblyTestServer? TestServer => null;

    internal SqlServerRowLockInterceptor Interceptor { get; } = new();

    internal SaveChangesConcurrencyTokenInterceptor ConcurrencyInterceptor { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(Xunit.TestContext.Current.CancellationToken);
        _connectionString = _container.GetConnectionString() + ";Encrypt=False;";
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
            .AddInterceptors(Interceptor, ConcurrencyInterceptor)
            .Options);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
