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
}
