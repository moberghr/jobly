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
        // JoblyModelCustomizer invokes these during OnModelCreating so the schema is
        // created exclusively for users of the addon. The builder IS the JoblyConfiguration
        // registered as IOptions, so mutating its EntityConfigurators list here is enough —
        // no extra Configure callback needed.
        builder.Configuration.EntityConfigurators.Add(ServiceConfiguration.AddCircuitBreakerStateEntity);

        builder.Services.AddScoped<ICircuitBreakerStore, CircuitBreakerStore<TContext>>();
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CircuitBreakerPipelineBehavior<,>));

        return builder;
    }
}
