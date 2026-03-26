using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.Handlers;

public static class JobHandlerServiceExtensions
{
    /// <summary>
    /// Scans the assembly for all IJobHandler&lt;T&gt; implementations and registers them in DI.
    /// </summary>
    public static IServiceCollection AddJobHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJobHandler<>)));

        foreach (var handlerType in handlerTypes)
        {
            var interfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJobHandler<>));

            foreach (var handlerInterface in interfaces)
            {
                services.AddTransient(handlerInterface, handlerType);
            }
        }

        return services;
    }

    /// <summary>
    /// Scans the assembly for all IJobPipelineBehavior&lt;T&gt; implementations and registers them.
    /// </summary>
    public static IServiceCollection AddJobPipelineBehaviors(this IServiceCollection services, Assembly assembly)
    {
        var behaviorTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJobPipelineBehavior<>)));

        foreach (var behaviorType in behaviorTypes)
        {
            var interfaces = behaviorType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJobPipelineBehavior<>));

            foreach (var behaviorInterface in interfaces)
            {
                services.AddTransient(behaviorInterface, behaviorType);
            }
        }

        return services;
    }
}
