using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.Handlers;

public static class HandlerServiceExtensions
{
    /// <summary>
    /// Scans the assembly for IJobHandler&lt;T&gt; and IMessageHandler&lt;T&gt; implementations and registers them in DI.
    /// </summary>
    public static IServiceCollection AddJobHandlers(this IServiceCollection services, Assembly assembly)
    {
        RegisterImplementations(services, assembly, typeof(IJobHandler<>));
        RegisterImplementations(services, assembly, typeof(IMessageHandler<>));

        return services;
    }

    /// <summary>
    /// Scans the assembly for IRequestHandler&lt;T,R&gt; and IStreamRequestHandler&lt;T,R&gt; implementations and registers them in DI.
    /// </summary>
    public static IServiceCollection AddMediatorHandlers(this IServiceCollection services, Assembly assembly)
    {
        RegisterImplementations(services, assembly, typeof(IRequestHandler<,>));
        RegisterImplementations(services, assembly, typeof(IStreamRequestHandler<,>));

        return services;
    }

    /// <summary>
    /// Scans the assembly for all pipeline behavior implementations and registers them in DI.
    /// </summary>
    public static IServiceCollection AddPipelineBehaviors(this IServiceCollection services, Assembly assembly)
    {
        RegisterImplementations(services, assembly, typeof(IPipelineBehavior<,>));
        RegisterImplementations(services, assembly, typeof(IPublishPipelineBehavior<>));
        RegisterImplementations(services, assembly, typeof(IStreamPipelineBehavior<,>));

        return services;
    }

    /// <summary>
    /// Convenience method: scans the assembly for all handler types (job, message, request, stream) and registers them in DI.
    /// </summary>
    public static IServiceCollection AddHandlers(this IServiceCollection services, Assembly assembly)
    {
        AddJobHandlers(services, assembly);
        AddMediatorHandlers(services, assembly);

        return services;
    }

    private static void RegisterImplementations(IServiceCollection services, Assembly assembly, Type openGenericInterface)
    {
        // Filter: skip abstract, interfaces, and nested private types (DI can't instantiate them).
        // Open generics (e.g. TimingBehavior<T, TResponse>) are allowed — .NET DI resolves
        // them as closed generics automatically when requested.
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
                // For open generic types, register using the generic type definitions
                // so the DI container can resolve them as open generics.
                var serviceType = iface.ContainsGenericParameters ? iface.GetGenericTypeDefinition() : iface;
                var implType = implementationType.ContainsGenericParameters ? implementationType.GetGenericTypeDefinition() : implementationType;
                services.AddTransient(serviceType, implType);
            }
        }
    }
}
