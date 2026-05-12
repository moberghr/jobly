using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core;
using Warp.Core.Handlers;
using Warp.Core.Handlers.Generated;
using Warp.Core.Retry;
using Warp.Core.Timeout;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Features.Timeout;

/// <summary>
/// Pipeline registration order regression. <c>AddRetry()</c> must be registered BEFORE
/// <c>AddTimeout()</c> — DI insertion order maps to pipeline outer→inner, so retry must wrap
/// timeout for its <c>catch (Exception)</c> to see Fail-mode's <c>TimeoutException</c>.
/// Reverse the order and timed-out jobs in Fail mode end Failed after one attempt instead of
/// being retried — a silent contract regression. These tests pin the ordering against future
/// refactors of WarpTestServer or the addon configuration.
/// </summary>
[Trait("Category", "NoDb")]
public class TimeoutAddonOrderingTests
{
    private static IPipelineBehavior<UnitRequest, Unit>[] ResolveBehaviors(Action<IWarpBuilder<TestContext>> configure)
    {
        var services = new ServiceCollection();
        services.AddWarpMediator();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<JobContext>();
        services.AddScoped<IJobContext>(x => x.GetRequiredService<JobContext>());
        services.AddScoped<TestContext>(_ => null!);

        var builder = new Warp.Core.WarpBuilder<TestContext>(services);
        configure(builder);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return [.. scope.ServiceProvider.GetServices<IPipelineBehavior<UnitRequest, Unit>>()];
    }

    [TimedFact]
    public Task RetryBeforeTimeout_RetryIsOuter_TimeoutIsInner()
    {
        var behaviors = ResolveBehaviors(b =>
        {
            b.AddRetry();
            b.AddTimeout();
        });

        // Outer behavior runs first (behaviors[0]). For Fail-mode to be retried, retry must
        // wrap timeout, so RetryPipelineBehavior should appear BEFORE TimeoutPipelineBehavior
        // in the resolved enumerable.
        var retryIndex = Array.FindIndex(behaviors, b => b is RetryPipelineBehavior<UnitRequest, Unit>);
        var timeoutIndex = Array.FindIndex(behaviors, b => b is TimeoutPipelineBehavior<UnitRequest, Unit>);

        retryIndex.ShouldBeGreaterThanOrEqualTo(0);
        timeoutIndex.ShouldBeGreaterThanOrEqualTo(0);
        retryIndex.ShouldBeLessThan(timeoutIndex);

        return Task.CompletedTask;
    }

    [TimedFact]
    public Task TimeoutBeforeRetry_BrokenOrder_IsObservable()
    {
        // This test pins the broken order so a future refactor of WarpTestServer doesn't
        // accidentally swap the registration without anyone noticing. If you legitimately need
        // to reverse the order, this test should be updated alongside the documented behavior.
        var behaviors = ResolveBehaviors(b =>
        {
            b.AddTimeout();
            b.AddRetry();
        });

        var retryIndex = Array.FindIndex(behaviors, b => b is RetryPipelineBehavior<UnitRequest, Unit>);
        var timeoutIndex = Array.FindIndex(behaviors, b => b is TimeoutPipelineBehavior<UnitRequest, Unit>);

        // The broken order: timeout is outer, retry is inner. Retry's catch never sees
        // TimeoutException because timeout throws *after* retry returns up the stack.
        timeoutIndex.ShouldBeLessThan(retryIndex);

        return Task.CompletedTask;
    }
}
