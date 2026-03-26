using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.Handlers;

public class JobDispatcher
{
    /// <summary>
    /// Discovers all registered IJobHandler&lt;T&gt; implementation types for a given message type.
    /// </summary>
    public List<Type> DiscoverHandlers(Type messageType, IServiceProvider provider)
    {
        var handlerInterfaceType = typeof(IJobHandler<>).MakeGenericType(messageType);
        var handlers = provider.GetServices(handlerInterfaceType);
        return handlers.Select(h => h!.GetType()).Distinct().ToList();
    }

    /// <summary>
    /// Executes a specific handler through the pipeline behavior chain.
    /// </summary>
    public async Task ExecuteHandler(object message, Type messageType, Type handlerType,
        IServiceProvider provider, CancellationToken cancellationToken)
    {
        // Resolve all handlers for this message type and find the one matching the concrete type
        var handlerInterfaceType = typeof(IJobHandler<>).MakeGenericType(messageType);
        var allHandlers = provider.GetServices(handlerInterfaceType);
        var handler = allHandlers.First(h => h!.GetType() == handlerType);

        // Find the HandleAsync method on the handler
        var handleMethod = handlerType.GetMethod("HandleAsync",
            new[] { messageType, typeof(CancellationToken) })!;

        // Build the innermost delegate: handler.HandleAsync(message, ct)
        JobHandlerDelegate innermost = () =>
            (Task)handleMethod.Invoke(handler, new object[] { message, cancellationToken })!;

        // Resolve pipeline behaviors
        var behaviorInterfaceType = typeof(IJobPipelineBehavior<>).MakeGenericType(messageType);
        var behaviors = provider.GetServices(behaviorInterfaceType).ToList();

        // Build the chain from innermost to outermost
        var chain = innermost;
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i]!;
            var behaviorHandleMethod = behavior.GetType().GetMethod("HandleAsync",
                new[] { messageType, typeof(JobHandlerDelegate), typeof(CancellationToken) })!;

            var next = chain;
            chain = () => (Task)behaviorHandleMethod.Invoke(behavior, new object[] { message, next, cancellationToken })!;
        }

        await chain();
    }
}
