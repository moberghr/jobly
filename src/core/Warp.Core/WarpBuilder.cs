using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Warp.Core;

/// <summary>
/// Common surface implemented by both <see cref="WarpBuilder{TContext}"/> (Core-only) and
/// the worker-side builder defined in the Warp.Worker package. Addon extension methods
/// (AddMutex, AddRetry, AddCircuitBreaker, AddNoRestart, UsePostgreSql, UseSqlServer) target
/// this interface so users can opt into them from either lambda.
/// </summary>
public interface IWarpBuilder<TContext>
    where TContext : DbContext
{
    IServiceCollection Services { get; }

    WarpConfiguration Configuration { get; }
}

/// <summary>
/// Builder passed to <see cref="ServiceConfiguration.AddWarp{TContext}"/>. Inherits
/// <see cref="WarpConfiguration"/> so config fields are set directly on the builder, and
/// exposes <see cref="Services"/> so addon extension methods can register their own services.
/// The builder instance is registered as the <c>IOptions&lt;WarpConfiguration&gt;</c> value,
/// so values set here become the runtime configuration with no extra copy step.
/// </summary>
public sealed class WarpBuilder<TContext> : WarpConfiguration, IWarpBuilder<TContext>
    where TContext : DbContext
{
    public WarpBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    WarpConfiguration IWarpBuilder<TContext>.Configuration => this;
}
