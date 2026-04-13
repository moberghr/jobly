using Jobly.Worker;
using Medallion.Threading;
using Medallion.Threading.Postgres;
using Medallion.Threading.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobly.Tests.Unit;

public class DistributedLockRegistrationTests
{
    [Fact]
    public void AddJoblyWorker_PostgreSql_ResolvesPostgresLockProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestContext>(o => o.UseNpgsql("Host=localhost;Database=test;Username=user;Password=secret"));
        services.AddJoblyWorker<TestContext>();

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IDistributedLockProvider>();

        provider.ShouldBeOfType<PostgresDistributedSynchronizationProvider>();
    }

    [Fact]
    public void AddJoblyWorker_SqlServer_ResolvesSqlServerLockProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestContext>(o => o.UseSqlServer("Server=localhost;Database=test;User Id=sa;Password=secret;TrustServerCertificate=True"));
        services.AddJoblyWorker<TestContext>();

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IDistributedLockProvider>();

        provider.ShouldBeOfType<SqlDistributedSynchronizationProvider>();
    }

    /// <summary>
    /// Verifies the fix reads the connection string from DbContextOptions extensions
    /// instead of resolving a DbContext. The old code created a scope and resolved the
    /// context — which, after Npgsql opens a pooled connection, can strip the password
    /// via PersistSecurityInfo=false.
    ///
    /// Poison the TestContext registration so any attempt to resolve it throws.
    /// The fix must succeed without ever resolving a DbContext.
    /// </summary>
    [Fact]
    public void AddJoblyWorker_PostgreSql_ResolvesFromOptionsExtension_NotFromContext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestContext>(o => o.UseNpgsql("Host=localhost;Database=test;Username=user;Password=secret"));
        services.AddJoblyWorker<TestContext>();

        // Poison: any attempt to resolve TestContext will throw
        services.AddScoped<TestContext>(_ => throw new InvalidOperationException("DbContext should not be resolved for connection string"));

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IDistributedLockProvider>();

        provider.ShouldBeOfType<PostgresDistributedSynchronizationProvider>();
    }

    /// <summary>
    /// Same as above but for SQL Server.
    /// </summary>
    [Fact]
    public void AddJoblyWorker_SqlServer_ResolvesFromOptionsExtension_NotFromContext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestContext>(o => o.UseSqlServer("Server=localhost;Database=test;User Id=sa;Password=secret;TrustServerCertificate=True"));
        services.AddJoblyWorker<TestContext>();

        // Poison: any attempt to resolve TestContext will throw
        services.AddScoped<TestContext>(_ => throw new InvalidOperationException("DbContext should not be resolved for connection string"));

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IDistributedLockProvider>();

        provider.ShouldBeOfType<SqlDistributedSynchronizationProvider>();
    }

    [Fact]
    public void AddJoblyWorker_OptionsExtensionPreservesPassword_AfterJoblyWrapsOptions()
    {
        var connectionString = "Host=localhost;Database=test;Username=user;Password=secret123";
        var services = new ServiceCollection();
        services.AddDbContext<TestContext>(o => o.UseNpgsql(connectionString));
        services.AddJoblyWorker<TestContext>();

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<DbContextOptions<TestContext>>();
        var extension = options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();

        extension.ShouldNotBeNull();
        extension.ConnectionString.ShouldBe(connectionString);
    }

    [Fact]
    public void AddJoblyWorker_PostgreSql_LockProviderCreatesLock()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestContext>(o => o.UseNpgsql("Host=localhost;Database=test;Username=user;Password=secret"));
        services.AddJoblyWorker<TestContext>();

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IDistributedLockProvider>();
        var distributedLock = provider.CreateLock("test-lock");

        distributedLock.ShouldNotBeNull();
        distributedLock.Name.ShouldBe("test-lock");
    }
}
