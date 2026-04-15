using Jobly.Core.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.TryAddScoped(typeof(IJobContext<>), typeof(JobContext<>));
        services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(RetryPublishBehavior<>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryPipelineBehavior<,>));

        return services;
    }
}
