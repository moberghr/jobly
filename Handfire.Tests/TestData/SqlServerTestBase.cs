using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Handfire.Core.Interceptors;
using Handfire.Tests.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Tests;

public class SqlServerTestBase : JobPublisher, IAsyncLifetime
{
    private static readonly SqlServerRowLockInterceptor _interceptor = new();
    private static readonly SaveChangesConcurrencyTokenInterceptor _concurrencyTokenInterceptor = new();

    private readonly MsSqlTestcontainer _dbContainer = new TestcontainersBuilder<MsSqlTestcontainer>()
        .WithDatabase(
            new MsSqlTestcontainerConfiguration
            {
                Database = "Handfire",
                Password = Guid.NewGuid().ToString("D"),
            })
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithEnvironment("ACCEPT_EULA", "Y")
        .WithCleanUp(true)
        .Build();

    protected override TestContext CreateContext()
    {
        var testContext = new TestContext(new DbContextOptionsBuilder<TestContext>()
           .UseSqlServer(_dbContainer.ConnectionString + ";Encrypt=False;")
           .AddInterceptors(_interceptor, _concurrencyTokenInterceptor).Options);

        testContext.Database.EnsureCreated();

        return testContext;
    }

    protected override TestContext CreateContextWithoutJobLocking()
    {
        var testContext = new TestContext(new DbContextOptionsBuilder<TestContext>()
           .UseSqlServer(_dbContainer.ConnectionString + ";Encrypt=False;")
           .AddInterceptors(_concurrencyTokenInterceptor).Options);

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
