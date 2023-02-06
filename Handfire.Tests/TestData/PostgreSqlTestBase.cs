using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Handfire.Core;
using Handfire.Core.Interceptors;
using Handfire.Tests.TestData;

namespace Handfire.Tests;

public class PostgreSqlTestBase : TestBase, IAsyncLifetime
{
    private static readonly PostgresRowLockInterceptor _interceptor = new();
    private static readonly SaveChangesConcurrencyTokenInterceptor _concurrencyTokenInterceptor = new();

    private readonly PostgreSqlTestcontainer _dbContainer = new TestcontainersBuilder<PostgreSqlTestcontainer>()
        .WithDatabase(
            new PostgreSqlTestcontainerConfiguration
            {
                Database = "Handfire",
                Username = "postgres",
                Password = Guid.NewGuid().ToString("D"),
            })
        .WithImage("postgres")  // latest
        .WithCleanUp(true)
        .Build();

    protected override TestContext CreateContext()
    {
        var dbcn = _dbContainer.ConnectionString;

        var testContext = new TestContext(new DbContextOptionsBuilder<TestContext>()
           .UseNpgsql(_dbContainer.ConnectionString)
           .AddInterceptors(_interceptor, _concurrencyTokenInterceptor).Options);

        testContext.Database.EnsureCreated();

        return testContext;
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
    }
}
