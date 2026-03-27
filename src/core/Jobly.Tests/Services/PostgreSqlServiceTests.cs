using Jobly.Core.Interceptors;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Jobly.Tests.Services;

public class PostgreSqlServiceTests : ServiceTests, IAsyncLifetime
{
    private static readonly PostgresRowLockInterceptor _interceptor = new();
    private static readonly SaveChangesConcurrencyTokenInterceptor _concurrencyTokenInterceptor = new();

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:latest")
        .Build();

    protected override TestContext CreateContext()
    {
        var testContext = new TestContext(new DbContextOptionsBuilder<TestContext>()
           .UseNpgsql(_dbContainer.GetConnectionString())
           .UseSnakeCaseNamingConvention()
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
