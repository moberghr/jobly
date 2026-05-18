using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Warp.Core.BackgroundServices;

public static class BackgroundServiceConfiguration
{
    /// <summary>
    /// Registers a <see cref="WarpBackgroundService"/> subclass with the Warp host and contributes
    /// the four background-service entities to the user's DbContext model. Call once per service
    /// type inside the <c>AddWarpWorker</c> lambda:
    /// <code>
    /// services.AddWarpWorker&lt;AppDbContext&gt;(opt =>
    /// {
    ///     opt.UsePostgreSql();
    ///     opt.AddBackgroundService&lt;KafkaDrainService&gt;();
    /// });
    /// </code>
    /// Calling <c>AddBackgroundService&lt;TContext, T&gt;()</c> twice for the same <typeparamref name="T"/> is
    /// a no-op — the second call is detected and skipped.
    /// </summary>
    /// <typeparam name="TContext">
    /// The user's <c>DbContext</c> subclass, inferred from the <paramref name="builder"/> receiver.
    /// </typeparam>
    /// <typeparam name="T">
    /// Concrete <see cref="WarpBackgroundService"/> subclass. Must be a class (not an interface or
    /// abstract type). The compile-time constraint prevents accidental registration of the base class
    /// itself or an unrelated type.
    /// </typeparam>
    public static IWarpBuilder<TContext> AddBackgroundService<TContext, T>(this IWarpBuilder<TContext> builder)
        where TContext : DbContext
        where T : WarpBackgroundService
    {
        // Idempotency guard: if T is already registered as a singleton we've been here before.
        if (builder.Services.Any(d => d.ServiceType == typeof(T) && d.Lifetime == ServiceLifetime.Singleton))
        {
            return builder;
        }

        // Register the concrete type as a singleton so it is created once per process.
        builder.Services.TryAddSingleton<T>();

        // Register the alias so the host can discover all WarpBackgroundService instances
        // via GetServices<WarpBackgroundService>() without knowing the concrete types.
        // The idempotency guard above ensures this executes at most once per T, so a
        // plain AddSingleton is safe — no risk of duplicate alias entries.
        builder.Services.AddSingleton<WarpBackgroundService>(sp => sp.GetRequiredService<T>());

        // Contribute the four background-service entities to the model exactly once, regardless
        // of how many AddBackgroundService<T> calls are made. We check for the presence of the
        // definition configurator as the "already registered" sentinel.
        if (!builder.Configuration.EntityConfigurators.Contains(ServiceConfiguration.AddBackgroundServiceDefinitionEntity))
        {
            builder.Configuration.EntityConfigurators.Add(ServiceConfiguration.AddBackgroundServiceDefinitionEntity);
            builder.Configuration.EntityConfigurators.Add(ServiceConfiguration.AddBackgroundServiceInstanceEntity);
            builder.Configuration.EntityConfigurators.Add(ServiceConfiguration.AddBackgroundServiceLeaseEntity);
            builder.Configuration.EntityConfigurators.Add(ServiceConfiguration.AddBackgroundServiceLogEntity);
        }

        // Register the dashboard query service exactly once. Presence of IBackgroundServiceQueryService
        // in DI is the addon-discovery marker used by GET /api/addons (WarpAddonsInfo.Services).
        builder.Services.TryAddScoped<IBackgroundServiceQueryService, BackgroundServiceQueryService<TContext>>();

        return builder;
    }
}
