using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Warp.Http;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Warp.Http services. Must be called once during application startup,
    /// before <see cref="EndpointRouteBuilderExtensions.MapWarpHttp"/>. Idempotent.
    /// </summary>
    public static IServiceCollection AddWarpHttp(this IServiceCollection services, Action<WarpHttpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new WarpHttpOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        return services;
    }
}
