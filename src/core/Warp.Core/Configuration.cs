using Microsoft.EntityFrameworkCore;

namespace Warp.Core;

public class WarpConfiguration
{
    public string DefaultQueue { get; set; } = "default";

    public string? Schema { get; set; } = "warp";

    /// <summary>
    /// How long completed and deleted jobs are retained before cleanup.
    /// Failed jobs are never auto-expired.
    /// </summary>
    public TimeSpan JobExpirationTimeout { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Model builder callbacks contributed by opt-in addons (e.g. CircuitBreaker).
    /// Invoked by WarpModelCustomizer after the core entities are registered. Addons
    /// append via the builder inside the <c>AddWarp</c>/<c>AddWarpWorker</c> lambda;
    /// exposed publicly so external addons (e.g. provider packages) can contribute too.
    /// </summary>
    public List<Action<ModelBuilder, string?>> EntityConfigurators { get; } = [];

    /// <summary>
    /// How long the host waits for each <c>WarpBackgroundService.ExecuteAsync</c> to return
    /// after the cancellation token is signalled during graceful shutdown. Services that do not
    /// observe cancellation are abandoned at process exit — same semantics as plain
    /// <c>BackgroundService.StopAsync</c> with a timeout.
    /// </summary>
    public TimeSpan BackgroundServiceShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Global default for the maximum number of captured log rows retained per
    /// <c>WarpBackgroundService</c> instance. Oldest rows are deleted by
    /// <c>ExpirationCleanup</c> when the count exceeds this value. Per-service overrides
    /// via <c>WarpBackgroundService.LogRetentionCountOverride</c> take precedence.
    /// </summary>
    public int BackgroundServiceLogRetentionCount { get; set; } = 1000;

    /// <summary>
    /// Global default for the maximum age of captured log rows. Rows older than this value
    /// are deleted by <c>ExpirationCleanup</c>. Per-service overrides via
    /// <c>WarpBackgroundService.LogRetentionAgeOverride</c> take precedence.
    /// </summary>
    public TimeSpan BackgroundServiceLogRetentionAge { get; set; } = TimeSpan.FromDays(7);
}
