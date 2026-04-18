using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.CircuitBreaker;

public static class CircuitBreakerServiceConfiguration
{
    public static IServiceCollection AddJoblyCircuitBreaker<TContext>(
        this IServiceCollection services,
        Action<CircuitBreakerOptions>? configure = null)
        where TContext : DbContext
    {
        services.AddOptions<CircuitBreakerOptions>();
        if (configure != null)
        {
            services.Configure(configure);
        }

        // Contribute the CircuitBreakerState entity only when the addon is opted in.
        // JoblyModelCustomizer invokes these during OnModelCreating so the schema is
        // created exclusively for users of the addon.
        services.Configure<JoblyConfiguration>(c => c.EntityConfigurators.Add(ServiceConfiguration.AddCircuitBreakerStateEntity));

        services.AddScoped<ICircuitBreakerStore, CircuitBreakerStore<TContext>>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CircuitBreakerPipelineBehavior<,>));

        return services;
    }
}
