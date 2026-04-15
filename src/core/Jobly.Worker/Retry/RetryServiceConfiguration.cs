using Jobly.Core.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Worker.Retry;

public static class RetryServiceConfiguration
{
    public static IServiceCollection AddJoblyRetry(this IServiceCollection services, Action<RetryOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RetryOptions>();
        }

        services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(RetryPublishBehavior<>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryPipelineBehavior<,>));

        return services;
    }
}
