using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core.Handlers;

namespace Warp.Core.Concurrency;

public static class ConcurrencyServiceConfiguration
{
    public static IWarpBuilder<TContext> AddConcurrency<TContext>(this IWarpBuilder<TContext> builder)
        where TContext : DbContext
    {
        // Contribute the ConcurrencyLimit entity only when the addon is opted in.
        // WarpModelCustomizer invokes these during OnModelCreating so the schema is created
        // exclusively for users of the addon. Mirrors AddCircuitBreaker's approach.
        builder.Configuration.EntityConfigurators.Add(ServiceConfiguration.AddConcurrencyLimitEntity);

        builder.Services.AddScoped<IConcurrencyLimitManager, ConcurrencyLimitManager<TContext>>();
        builder.Services.AddScoped<ConcurrencyLimitResolver>();
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ConcurrencyPipelineBehavior<,>));
        builder.Services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(ConcurrencyPublishBehavior<>));

        return builder;
    }
}
