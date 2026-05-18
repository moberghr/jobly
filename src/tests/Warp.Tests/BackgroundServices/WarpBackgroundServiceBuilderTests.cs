using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Warp.Core;
using Warp.Core.BackgroundServices;

namespace Warp.Tests.BackgroundServices;

[Trait("Category", "NoDb")]
public class WarpBackgroundServiceBuilderTests
{
    private static WarpBuilder<TestContext> CreateBuilder()
    {
        var services = new ServiceCollection();
        return new WarpBuilder<TestContext>(services);
    }

    [TimedFact]
    public void AddBackgroundService_RegistersTypeAsSingleton()
    {
        var builder = CreateBuilder();

        builder.AddBackgroundService<TestContext, TestService>();

        var sp = builder.Services.BuildServiceProvider();
        var first = sp.GetRequiredService<TestService>();
        var second = sp.GetRequiredService<TestService>();

        first.ShouldBeSameAs(second);
    }

    [TimedFact]
    public void AddBackgroundService_ResolvesAsWarpBackgroundService()
    {
        var builder = CreateBuilder();

        builder.AddBackgroundService<TestContext, TestService>();

        var sp = builder.Services.BuildServiceProvider();
        var concrete = sp.GetRequiredService<TestService>();
        var alias = sp.GetServices<WarpBackgroundService>().Single();

        alias.ShouldBeSameAs(concrete);
    }

    [TimedFact]
    public void AddBackgroundService_CalledTwiceForSameType_IsIdempotent()
    {
        var builder = CreateBuilder();

        builder.AddBackgroundService<TestContext, TestService>();
        builder.AddBackgroundService<TestContext, TestService>();

        var sp = builder.Services.BuildServiceProvider();
        var aliases = sp.GetServices<WarpBackgroundService>()
            .Where(x => x is TestService)
            .ToList();

        aliases.Count.ShouldBe(1);
    }

    [TimedFact]
    public void AddBackgroundService_TwoDifferentServices_BothResolveViaAlias()
    {
        var builder = CreateBuilder();

        builder.AddBackgroundService<TestContext, TestService>();
        builder.AddBackgroundService<TestContext, AnotherTestService>();

        var sp = builder.Services.BuildServiceProvider();
        var aliases = sp.GetServices<WarpBackgroundService>().ToList();

        aliases.Count.ShouldBe(2);
        aliases.ShouldContain(x => x is TestService);
        aliases.ShouldContain(x => x is AnotherTestService);
    }

    [TimedFact]
    public void WarpBackgroundService_NameDefault_IsTypeName()
    {
        var service = new TestService();

        service.Name.ShouldBe(nameof(TestService));
    }

    [TimedFact]
    public void WarpBackgroundService_ScopeDefault_IsPerServer()
    {
        var service = new TestService();

        service.Scope.ShouldBe(ServiceScope.PerServer);
    }

    [TimedFact]
    public void WarpBackgroundService_MinLogLevelDefault_IsInformation()
    {
        var service = new TestService();

        service.MinLogLevel.ShouldBe(LogLevel.Information);
    }

    [TimedFact]
    public void WarpBackgroundService_NameCustomOverride_Honored()
    {
        var service = new CustomNameService();

        service.Name.ShouldBe("my-custom-name");
    }

    [TimedFact]
    public void AddBackgroundService_ContributesEntityConfiguratorsExactlyOnce()
    {
        var builder = CreateBuilder();

        builder.AddBackgroundService<TestContext, TestService>();
        builder.AddBackgroundService<TestContext, AnotherTestService>();

        builder.EntityConfigurators
            .Count(c => c == ServiceConfiguration.AddBackgroundServiceDefinitionEntity)
            .ShouldBe(1);
    }

    public sealed class TestService : WarpBackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
    }

    public sealed class AnotherTestService : WarpBackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
    }

    public sealed class CustomNameService : WarpBackgroundService
    {
        public override string Name => "my-custom-name";

        protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
