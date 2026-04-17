using Jobly.Core.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.NoRestart;

public static class NoRestartServiceConfiguration
{
    public static IServiceCollection AddJoblyNoRestart(this IServiceCollection services)
    {
        services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(NoRestartPublishBehavior<>));

        return services;
    }
}
