using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.CircuitBreaker;

public static class CircuitBreakerServiceConfiguration
{
    public static IJoblyBuilder<TContext> AddCircuitBreaker<TContext>(
        this IJoblyBuilder<TContext> builder,
        Action<CircuitBreakerOptions>? configure = null)
        where TContext : DbContext
    {
        builder.Services.AddOptions<CircuitBreakerOptions>();
        if (configure != null)
        {
            builder.Services.Configure(configure);
        }

        // Contribute the CircuitBreakerState entity only when the addon is opted in.
        // JoblyModelCustomizer invokes these during OnModelCreating so the schema is created
        // exclusively for users of the addon. EntityConfigurators is a reference-type list on
        // the builder object, which was registered earlier via Options.Create on the same
        // instance — so mutating it here is visible later when EF Core builds the model. This
        // relies on JoblyModelCustomizer running lazily during model build, after DI is fully
        // configured. If that ever becomes eager, the timing guarantee breaks.
        builder.Configuration.EntityConfigurators.Add(ServiceConfiguration.AddCircuitBreakerStateEntity);

        builder.Services.AddScoped<ICircuitBreakerStore, CircuitBreakerStore<TContext>>();
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CircuitBreakerPipelineBehavior<,>));

        return builder;
    }
}
