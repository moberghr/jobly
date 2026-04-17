using Jobly.Core.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.Mutex;

public static class MutexServiceConfiguration
{
    public static IServiceCollection AddJoblyMutex(this IServiceCollection services)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MutexPipelineBehavior<,>));
        services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(MutexPublishBehavior<>));

        return services;
    }
}
