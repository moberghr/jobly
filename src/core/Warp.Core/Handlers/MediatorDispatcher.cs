using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Warp.Core.Handlers;

public static class MediatorDispatcher
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> _typedPipelineMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> _typedStreamPipelineMethodCache = new();

    private static readonly MethodInfo ExecuteTypedPipelineMethodInfo =
        typeof(MediatorDispatcher).GetMethod(nameof(ExecuteTypedPipeline), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ExecuteTypedStreamPipelineMethodInfo =
        typeof(MediatorDispatcher).GetMethod(nameof(ExecuteTypedStreamPipeline), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Executes an IRequestHandler through the pipeline. Returns the typed response.
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
