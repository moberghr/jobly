using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core;

/// <summary>
/// Common surface implemented by both <see cref="JoblyBuilder{TContext}"/> (Core-only) and
/// the worker-side builder defined in the Jobly.Worker package. Addon extension methods
/// (AddMutex, AddRetry, AddCircuitBreaker, AddNoRestart, UsePostgreSql, UseSqlServer) target
/// this interface so users can opt into them from either lambda.
/// </summary>
public interface IJoblyBuilder<TContext>
    where TContext : DbContext
{
    IServiceCollection Services { get; }

    JoblyConfiguration Configuration { get; }
}

/// <summary>
/// Builder passed to <see cref="ServiceConfiguration.AddJobly{TContext}"/>. Inherits
/// <see cref="JoblyConfiguration"/> so config fields are set directly on the builder, and
/// exposes <see cref="Services"/> so addon extension methods can register their own services.
/// The builder instance is registered as the <c>IOptions&lt;JoblyConfiguration&gt;</c> value,
/// so values set here become the runtime configuration with no extra copy step.
/// </summary>
public sealed class JoblyBuilder<TContext> : JoblyConfiguration, IJoblyBuilder<TContext>
    where TContext : DbContext
{
    public JoblyBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    JoblyConfiguration IJoblyBuilder<TContext>.Configuration => this;
}
