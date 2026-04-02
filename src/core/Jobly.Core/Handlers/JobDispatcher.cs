using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.Handlers;

public static class JobDispatcher
{
    private static readonly ConcurrentDictionary<Type, Type> _handlerInterfaceCache = new();
    private static readonly ConcurrentDictionary<(Type HandlerType, Type MessageType), MethodInfo> _handleMethodCache = new();
    private static readonly ConcurrentDictionary<Type, Type> _jobHandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Type> _messageHandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Type> _pipelineBehaviorTypeCache = new();

    /// <summary>
    /// Discovers all registered IMessageHandler&lt;T&gt; implementation types for a message type.
    /// </summary>
    public static List<Type> DiscoverMessageHandlers(Type messageType, IServiceProvider provider)
    {
        var handlerInterfaceType = _messageHandlerTypeCache.GetOrAdd(messageType,
            t => typeof(IMessageHandler<>).MakeGenericType(t));
        var handlers = provider.GetServices(handlerInterfaceType);
        return [.. handlers.Select(h => h!.GetType()).Distinct()];
    }

    /// <summary>
    /// Discovers the single IJobHandler&lt;T&gt; for a job type.
    /// </summary>
    public static Type? DiscoverJobHandler(Type jobType, IServiceProvider provider)
    {
        var handlerInterfaceType = _jobHandlerTypeCache.GetOrAdd(jobType,
            t => typeof(IJobHandler<>).MakeGenericType(t));
        var handler = provider.GetService(handlerInterfaceType);
        return handler?.GetType();
    }

    /// <summary>
    /// Gets the handler interface (IMessageHandler or IJobHandler) that a concrete handler type implements.
    /// Cached for performance.
    /// </summary>
    public static Type GetHandlerInterface(Type handlerType)
    {
        return _handlerInterfaceCache.GetOrAdd(handlerType, t => t.GetInterfaces()
            .First(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(IMessageHandler<>) ||
                 i.GetGenericTypeDefinition() == typeof(IJobHandler<>))));
    }

    /// <summary>
    /// Executes a specific handler through the pipeline behavior chain.
    /// Derives the handler interface automatically from the handler type.
    /// </summary>
    public static Task ExecuteHandler(
        object message,
        Type messageType,
        Type handlerType,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var handlerInterfaceType = GetHandlerInterface(handlerType);
        return ExecuteHandlerCore(message, messageType, handlerType, handlerInterfaceType, provider, cancellationToken);
    }

    /// <summary>
    /// Executes a specific handler through the pipeline behavior chain.
    /// Works for both IMessageHandler and IJobHandler (same reflection pattern).
    /// </summary>
    public static async Task ExecuteHandlerCore(
        object message,
        Type messageType,
        Type handlerType,
        Type handlerInterfaceType,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        // Resolve handler via its interface to get the DI-registered instance
        var allHandlers = provider.GetServices(handlerInterfaceType);
        var handler = allHandlers.First(h => h!.GetType() == handlerType);

        // Find the HandleAsync method on the handler (cached)
        var handleMethod = _handleMethodCache.GetOrAdd(
            (handlerType, messageType),
            key => key.HandlerType.GetMethod(
                "HandleAsync",
                [key.MessageType, typeof(CancellationToken)])!);

        // Build the innermost delegate: handler.HandleAsync(message, ct)
        Task Innermost() =>
            (Task)handleMethod.Invoke(handler, [message, cancellationToken])!;

        // Resolve pipeline behaviors
        var behaviorInterfaceType = _pipelineBehaviorTypeCache.GetOrAdd(messageType,
            t => typeof(IPipelineBehavior<>).MakeGenericType(t));
        var behaviors = provider.GetServices(behaviorInterfaceType).ToList();

        // Build the chain from innermost to outermost
        var chain = (JobHandlerDelegate)Innermost;
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i]!;
            var behaviorHandleMethod = _handleMethodCache.GetOrAdd(
                (behavior.GetType(), messageType),
                key => key.HandlerType.GetMethod(
                    "HandleAsync",
                    [key.MessageType, typeof(JobHandlerDelegate), typeof(CancellationToken)])!);

            var next = chain;
            chain = () => (Task)behaviorHandleMethod.Invoke(behavior, [message, next, cancellationToken])!;
        }

        await chain();
    }

    /// <summary>
    /// Execute a message handler (IMessageHandler&lt;T&gt;).
    /// </summary>
    public static Task ExecuteMessageHandler(
        object message,
        Type messageType,
        Type handlerType,
        IServiceProvider provider,
        CancellationToken ct)
    {
        var handlerInterfaceType = _messageHandlerTypeCache.GetOrAdd(messageType,
            t => typeof(IMessageHandler<>).MakeGenericType(t));
        return ExecuteHandlerCore(message, messageType, handlerType, handlerInterfaceType, provider, ct);
    }

    /// <summary>
    /// Execute a job handler (IJobHandler&lt;T&gt;).
    /// </summary>
    public static Task ExecuteJobHandler(
        object message,
        Type messageType,
        Type handlerType,
        IServiceProvider provider,
        CancellationToken ct)
    {
        var handlerInterfaceType = _jobHandlerTypeCache.GetOrAdd(messageType,
            t => typeof(IJobHandler<>).MakeGenericType(t));
        return ExecuteHandlerCore(message, messageType, handlerType, handlerInterfaceType, provider, ct);
    }
}
