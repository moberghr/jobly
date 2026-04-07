using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.Handlers;

public static class JobHandlerServiceExtensions
{
    /// <summary>
    /// Scans the assembly for all IJobHandler&lt;T&gt;, IMessageHandler&lt;T&gt;, and IRequestHandler&lt;T,R&gt; implementations and registers them in DI.
    /// </summary>
    public static IServiceCollection AddJobHandlers(this IServiceCollection services, Assembly assembly)
    {
        RegisterImplementations(services, assembly, typeof(IJobHandler<>));
        RegisterImplementations(services, assembly, typeof(IMessageHandler<>));
        RegisterImplementations(services, assembly, typeof(IRequestHandler<,>));
        return services;
    }

    /// <summary>
    /// Scans the assembly for all IPipelineBehavior&lt;T, TResponse&gt; implementations and registers them.
    /// </summary>
    public static IServiceCollection AddPipelineBehaviors(this IServiceCollection services, Assembly assembly)
    {
        RegisterImplementations(services, assembly, typeof(IPipelineBehavior<,>));
        return services;
    }

    private static void RegisterImplementations(IServiceCollection services, Assembly assembly, Type openGenericInterface)
    {
        var implementationTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsNestedPrivate: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface));

        foreach (var implementationType in implementationTypes)
        {
            var interfaces = implementationType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface);

            foreach (var iface in interfaces)
            {
                services.AddTransient(iface, implementationType);
            }
        }
    }
}
