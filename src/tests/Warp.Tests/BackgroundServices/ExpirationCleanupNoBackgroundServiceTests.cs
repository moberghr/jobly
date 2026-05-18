using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using Warp.Worker;
using Warp.Worker.Services;

namespace Warp.Tests.BackgroundServices;

/// <summary>
/// NoDb unit tests for the <see cref="ExpirationCleanup{TContext}"/> guard that skips
/// <c>BackgroundServiceLog</c> cleanup when the entity is not in the model
/// (i.e. <c>AddBackgroundService</c> was not called).
/// </summary>
[Trait("Category", "NoDb")]
public class ExpirationCleanupNoBackgroundServiceTests
{
    /// <summary>
    /// Regression test for the guard introduced in FIX 1: <c>CleanupBackgroundServiceLogsAsync</c>
    /// must not throw when <c>BackgroundServiceLog</c> is absent from the EF model.
    /// Deployments that never call <c>AddBackgroundService&lt;T&gt;()</c> do not have the entity
    /// registered, so hitting <c>_context.Set&lt;BackgroundServiceLog&gt;()</c> without the guard
    /// would throw an <see cref="InvalidOperationException"/>.
    /// </summary>
    [TimedFact]
    public async Task ExpirationCleanup_BackgroundServiceLogEntityNotInModel_NoThrow()
    {
        // Arrange: in-memory context WITHOUT the BackgroundServiceLog entity.
        var options = new DbContextOptionsBuilder<MinimalDbContext>()
            .UseInMemoryDatabase(databaseName: $"NoBackgroundService_{Guid.NewGuid():N}")
            .Options;

        await using var ctx = new MinimalDbContext(options);

        // Confirm the entity is genuinely absent — the guard relies on this.
        ctx.Model.FindEntityType(typeof(Warp.Core.Data.Entities.BackgroundServiceLog)).ShouldBeNull(
            "MinimalDbContext must not include BackgroundServiceLog in its model");

        var config = new WarpWorkerConfiguration
        {
            BackgroundServiceLogRetentionCount = 1000,
            BackgroundServiceLogRetentionAge = TimeSpan.FromDays(7),
        };

        var cleanup = new ExpirationCleanup<MinimalDbContext>(
            ctx,
            TimeProvider.System,
            Options.Create(config));

        // Act + Assert: must complete without throwing.
        await Should.NotThrowAsync(
            () => cleanup.CleanupBackgroundServiceLogsAsync(Xunit.TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// Minimal <see cref="DbContext"/> without any BackgroundService entities. Simulates a
/// deployment that called <c>AddWarp</c> without <c>AddBackgroundService&lt;T&gt;()</c>.
/// </summary>
file sealed class MinimalDbContext : DbContext
{
    public MinimalDbContext(DbContextOptions<MinimalDbContext> options)
        : base(options)
    {
    }
}
