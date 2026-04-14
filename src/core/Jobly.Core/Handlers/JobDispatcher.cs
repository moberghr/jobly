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
    private static readonly ConcurrentDictionary<Type, MethodInfo> _typedPipelineMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> _typedStreamPipelineMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> _typedHandlerCoreMethodCache = new();

    private static readonly MethodInfo ExecuteTypedPipelineMethodInfo =
        typeof(JobDispatcher).GetMethod(nameof(ExecuteTypedPipeline), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ExecuteTypedStreamPipelineMethodInfo =
        typeof(JobDispatcher).GetMethod(nameof(ExecuteTypedStreamPipeline), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ExecuteTypedHandlerCoreMethodInfo =
        typeof(JobDispatcher).GetMethod(nameof(ExecuteTypedHandlerCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Discovers all registered IMessageHandler&lt;T&gt; implementation types for a message type.
    /// </summary>
    public static List<Type> DiscoverMessageHandlers(Type messageType, IServiceProvider provider)
    {
        var handlerInterfaceType = _messageHandlerTypeCache.GetOrAdd(
            messageType,
            t => typeof(IMessageHandler<>).MakeGenericType(t));
        var handlers = provider.GetServices(handlerInterfaceType);
        return [.. handlers.Select(h => h!.GetType()).Distinct()];
    }

    /// <summary>
    /// Discovers the single IJobHandler&lt;T&gt; for a job type.
    /// </summary>
    public static Type? DiscoverJobHandler(Type jobType, IServiceProvider provider)
    {
        var handlerInterfaceType = _jobHandlerTypeCache.GetOrAdd(
            jobType,
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
    /// Executes a job/message handler through the unified pipeline.
    /// Wraps void-returning handlers to return Unit.
    /// Uses MakeGenericMethod to enter a fully-typed pipeline where direct calls replace reflection.
    /// </summary>
    public static Task ExecuteHandlerCore(
        object message,
        Type messageType,
        Type handlerType,
        Type handlerInterfaceType,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var typedMethod = _typedHandlerCoreMethodCache.GetOrAdd(
            messageType,
            t => ExecuteTypedHandlerCoreMethodInfo.MakeGenericMethod(t));

        return (Task)typedMethod.Invoke(null, [message, handlerType, handlerInterfaceType, provider, cancellationToken])!;
    }

    /// <summary>
    /// Fully-typed handler core for job/message types. Uses direct calls instead of MethodInfo.Invoke.
    /// </summary>
    private static async Task ExecuteTypedHandlerCore<TRequest>(
        object message,
        Type handlerType,
        Type handlerInterfaceType,
        IServiceProvider provider,
        CancellationToken cancellationToken)
        where TRequest : IRequest<Unit>
    {
        // Resolve handler via its interface to get the DI-registered instance
        var allHandlers = provider.GetServices(handlerInterfaceType);
        var handler = allHandlers.First(h => h!.GetType() == handlerType);

        // Find the HandleAsync method on the handler (cached)
        var handleMethod = _handleMethodCache.GetOrAdd(
            (handlerType, typeof(TRequest)),
            key => key.HandlerType.GetMethod(
                "HandleAsync",
                [key.MessageType, typeof(CancellationToken)])!);

        // Build pipeline with typed delegate
        var typedMessage = (TRequest)message;
        RequestHandlerDelegate<TRequest, Unit> innermost = async (req, ct) =>
        {
            await ((Task)handleMethod.Invoke(handler, [req, ct])!);
            return Unit.Value;
        };

        var behaviors = provider.GetServices<IPipelineBehavior<TRequest, Unit>>().ToArray();
        var chain = innermost;
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var b = behaviors[i];
            var next = chain;
            chain = (req, ct) => b.HandleAsync(req, next, ct);
        }

        await chain(typedMessage, cancellationToken);
    }

    /// <summary>
    /// Executes an IRequestHandler through the unified pipeline. Returns the typed response.
    /// Uses MakeGenericMethod to enter a fully-typed pipeline where direct calls replace reflection.
    /// </summary>
    public static Task<TResponse> ExecuteRequestHandler<TResponse>(
        object request,
        Type requestType,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var typedMethod = _typedPipelineMethodCache.GetOrAdd(
            requestType,
            t => ExecuteTypedPipelineMethodInfo.MakeGenericMethod(t, typeof(TResponse)));

        return (Task<TResponse>)typedMethod.Invoke(null, [request, provider, cancellationToken])!;
    }

    /// <summary>
    /// Fully-typed pipeline for IRequest types. Uses direct calls instead of MethodInfo.Invoke for
    /// handler and behavior execution.
    /// </summary>
    private static async Task<TResponse> ExecuteTypedPipeline<TRequest, TResponse>(
        object request,
        IServiceProvider provider,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var handler = provider.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException($"No handler registered for {typeof(TRequest).Name}");

        var typedRequest = (TRequest)request;
        RequestHandlerDelegate<TRequest, TResponse> chain = handler.HandleAsync;

        var behaviors = provider.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var b = behaviors[i];
            var next = chain;
            chain = (req, ct) => b.HandleAsync(req, next, ct);
        }

        return await chain(typedRequest, cancellationToken);
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
        var handlerInterfaceType = _messageHandlerTypeCache.GetOrAdd(
            messageType,
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
        var handlerInterfaceType = _jobHandlerTypeCache.GetOrAdd(
            messageType,
            t => typeof(IJobHandler<>).MakeGenericType(t));
        return ExecuteHandlerCore(message, messageType, handlerType, handlerInterfaceType, provider, ct);
    }

    /// <summary>
    /// Executes an IStreamRequestHandler through the stream pipeline. Returns a typed async enumerable.
    /// Uses MakeGenericMethod to enter a fully-typed pipeline where direct calls replace reflection.
    /// </summary>
    public static IAsyncEnumerable<TResponse> ExecuteStreamHandler<TResponse>(
        object request,
        Type requestType,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var typedMethod = _typedStreamPipelineMethodCache.GetOrAdd(
            requestType,
            t => ExecuteTypedStreamPipelineMethodInfo.MakeGenericMethod(t, typeof(TResponse)));

        try
        {
            return (IAsyncEnumerable<TResponse>)typedMethod.Invoke(null, [request, provider, cancellationToken])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();

            throw; // Unreachable, satisfies compiler
        }
    }

    /// <summary>
    /// Fully-typed pipeline for IStreamRequest types. Composes IStreamPipelineBehavior (enumeration-level)
    /// inside IPipelineBehavior (request-level), then executes the combined chain.
    /// </summary>
    private static IAsyncEnumerable<TResponse> ExecuteTypedStreamPipeline<TRequest, TResponse>(
        object request,
        IServiceProvider provider,
        CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse>
    {
        var handler = provider.GetService<IStreamRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException($"No stream handler registered for {typeof(TRequest).Name}");

        var typedRequest = (TRequest)request;

        // Inner chain: IStreamPipelineBehavior → handler
        StreamHandlerDelegate<TRequest, TResponse> streamChain = handler.HandleAsync;

        var streamBehaviors = provider.GetServices<IStreamPipelineBehavior<TRequest, TResponse>>().ToArray();
        for (var i = streamBehaviors.Length - 1; i >= 0; i--)
        {
            var b = streamBehaviors[i];
            var next = streamChain;
            streamChain = (req, ct) => b.HandleAsync(req, next, ct);
        }

        // Outer chain: IPipelineBehavior (request-level, wraps the stream envelope)
        var requestBehaviors = provider.GetServices<IPipelineBehavior<TRequest, IAsyncEnumerable<TResponse>>>().ToArray();
        if (requestBehaviors.Length == 0)
        {
            return streamChain(typedRequest, cancellationToken);
        }

        RequestHandlerDelegate<TRequest, IAsyncEnumerable<TResponse>> requestChain =
            (req, ct) => Task.FromResult(streamChain(req, ct));
        for (var i = requestBehaviors.Length - 1; i >= 0; i--)
        {
            var b = requestBehaviors[i];
            var next = requestChain;
            requestChain = (req, ct) => b.HandleAsync(req, next, ct);
        }

        return UnwrapStreamTask(requestChain(typedRequest, cancellationToken), cancellationToken);
    }

    private static async IAsyncEnumerable<TResponse> UnwrapStreamTask<TResponse>(
        Task<IAsyncEnumerable<TResponse>> task,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerable = await task;
        await foreach (var item in enumerable.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }
}
