using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core.BackgroundServices;

namespace Warp.Tests.BackgroundServices;

/// <summary>
/// Verifies that the standard .NET DI captive-scoped-dependency foot-gun is caught at startup
/// when <c>ValidateScopes = true</c> — a singleton <c>WarpBackgroundService</c> that takes a
/// scoped <c>DbContext</c> directly in its constructor will throw when the singleton is first
/// resolved from the root container.
/// </summary>
[Trait("Category", "NoDb")]
public class CaptiveScopedDependencyTests
{
    [TimedFact]
    public void BuildServiceProvider_WithScopedDepOnBackgroundService_ThrowsWithValidateScopes()
    {
        var services = new ServiceCollection();

        // Register a minimal DbContext as Scoped (the default AddDbContext lifetime).
        services.AddDbContext<MinimalContext>(options =>
            options.UseInMemoryDatabase("captive-dep-test"));

        // Register a WarpBackgroundService subclass that captures the scoped DbContext
        // directly in its constructor — this is the captive-dep pattern we document against.
        services.AddSingleton<CaptiveDepService>();

        // ValidateScopes=true causes the root container to throw InvalidOperationException
        // when a singleton attempts to consume a scoped dependency. The error surfaces on
        // first resolution (not at build time), since .NET DI validates lazily.
        var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Should.Throw<InvalidOperationException>(() => sp.GetRequiredService<CaptiveDepService>());
    }

    public sealed class MinimalContext : DbContext
    {
        public MinimalContext(DbContextOptions<MinimalContext> options)
            : base(options)
        {
        }
    }

    public sealed class CaptiveDepService : WarpBackgroundService
    {
        // Constructor captures a scoped DbContext into a singleton — the exact pattern
        // ValidateScopes=true is designed to catch.
        public CaptiveDepService(MinimalContext context)
        {
            _ = context;
        }

        protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
