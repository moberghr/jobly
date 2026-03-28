using Jobly.Core.Interceptors;
using Jobly.Tests.Jobs;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Jobly.Tests;

[Trait("Category", "SqlServer")]
public class SqlServerTests : JoblyTests, IAsyncLifetime
{
    private static readonly SqlServerRowLockInterceptor _interceptor = new();
    private static readonly SaveChangesConcurrencyTokenInterceptor _concurrencyTokenInterceptor = new();

    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public SqlServerTests() : base(null!)
    {
        // SQL Server tests are skipped (Category=SqlServer).
        // TODO: convert to Respawn fixture pattern.
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
    }
}
