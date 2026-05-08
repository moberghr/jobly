using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core.Logging;

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
        var requestTypeName = typeof(TRequest).Name;
        var responseTypeName = typeof(TResponse).Name;
        using var mediatorActivity = WarpTelemetry.StartMediatorActivity(
            requestTypeName,
            responseTypeName,
            WarpTelemetryAttributes.MediatorKindRequest);

        // Hot-path guard: skip the per-call Stopwatch allocation and KVP-array allocations
        // (params arrays for Add/Record) when no MeterListener is attached. `Instrument.Enabled`
        // returns true only when at least one listener has called EnableMeasurementEvents on
        // the instrument, so `Mediator.Send` runs allocation-free in deployments that don't
        // wire `meterBuilder.AddMeter("Warp")`. Mediator is HTTP-request-frequency in
        // Warp.Http hosts, so this is the most allocation-sensitive call site in the library.
        var metricsEnabled = WarpTelemetry.MediatorDuration.Enabled || WarpTelemetry.MediatorInFlight.Enabled;
        Stopwatch? stopwatch = null;
        if (metricsEnabled)
        {
            WarpTelemetry.MediatorInFlight.Add(
                1,
                new KeyValuePair<string, object?>("kind", WarpTelemetryAttributes.MediatorKindRequest),
                new KeyValuePair<string, object?>("request_type", requestTypeName));
            stopwatch = Stopwatch.StartNew();
        }

        var status = "succeeded";

        try
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = "cancelled";
            mediatorActivity?.SetStatus(ActivityStatusCode.Error, "cancelled");

            throw;
        }
        catch (Exception ex)
        {
            status = "failed";
            mediatorActivity?.SetStatus(ActivityStatusCode.Error, WarpTelemetry.TruncateMessage(ex.Message, 256));
            mediatorActivity?.SetTag(WarpTelemetryAttributes.ErrorType, ex.GetType().FullName);

            throw;
        }
        finally
        {
            if (metricsEnabled)
            {
                stopwatch!.Stop();
                WarpTelemetry.MediatorDuration.Record(
                    stopwatch.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("kind", WarpTelemetryAttributes.MediatorKindRequest),
                    new KeyValuePair<string, object?>("request_type", requestTypeName),
                    new KeyValuePair<string, object?>("status", status));
                WarpTelemetry.MediatorInFlight.Add(
                    -1,
                    new KeyValuePair<string, object?>("kind", WarpTelemetryAttributes.MediatorKindRequest),
                    new KeyValuePair<string, object?>("request_type", requestTypeName));
            }
        }
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
        IAsyncEnumerable<TResponse> source;
        if (requestBehaviors.Length == 0)
        {
            source = streamChain(typedRequest, cancellationToken);
        }
        else
        {
            RequestHandlerDelegate<TRequest, IAsyncEnumerable<TResponse>> requestChain =
                (req, ct) => Task.FromResult(streamChain(req, ct));
            for (var i = requestBehaviors.Length - 1; i >= 0; i--)
            {
                var b = requestBehaviors[i];
                var next = requestChain;
                requestChain = (req, ct) => b.HandleAsync(req, next, ct);
            }

            source = UnwrapStreamTask(requestChain(typedRequest, cancellationToken), cancellationToken);
        }

        return InstrumentStream<TRequest, TResponse>(source, cancellationToken);
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

    /// <summary>
    /// Wraps a stream with the mediator activity + duration histogram + in-flight gauge so the
    /// span lives across the full enumeration. Disposing the enumerator (early break, exception)
    /// closes the span via try/finally — no leaks even if the consumer abandons the stream.
    /// </summary>
    private static async IAsyncEnumerable<TResponse> InstrumentStream<TRequest, TResponse>(
        IAsyncEnumerable<TResponse> source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse>
    {
        var requestTypeName = typeof(TRequest).Name;
        var responseTypeName = typeof(TResponse).Name;
        var activity = WarpTelemetry.StartMediatorActivity(
            requestTypeName,
            responseTypeName,
            WarpTelemetryAttributes.MediatorKindStream);

        // Same hot-path guard as ExecuteTypedPipeline — skip Stopwatch + KVP allocs when no
        // MeterListener is attached. Mediator streams are HTTP-frequency in Warp.Http.
        var metricsEnabled = WarpTelemetry.MediatorDuration.Enabled || WarpTelemetry.MediatorInFlight.Enabled;
        Stopwatch? stopwatch = null;
        if (metricsEnabled)
        {
            WarpTelemetry.MediatorInFlight.Add(
                1,
                new KeyValuePair<string, object?>("kind", WarpTelemetryAttributes.MediatorKindStream),
                new KeyValuePair<string, object?>("request_type", requestTypeName));
            stopwatch = Stopwatch.StartNew();
        }

        var status = "succeeded";

        // Iterator catches must not contain `yield return`. We exit the catch via `throw;` after
        // tagging the activity, then the finally records duration and decrements in-flight.
        IAsyncEnumerator<TResponse>? enumerator = null;
        try
        {
            try
            {
                enumerator = source.GetAsyncEnumerator(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                status = "cancelled";

                throw;
            }
            catch (Exception ex)
            {
                status = "failed";
                activity?.SetStatus(ActivityStatusCode.Error, WarpTelemetry.TruncateMessage(ex.Message, 256));
                activity?.SetTag(WarpTelemetryAttributes.ErrorType, ex.GetType().FullName);

                throw;
            }

            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    status = "cancelled";

                    throw;
                }
                catch (Exception ex)
                {
                    status = "failed";
                    activity?.SetStatus(ActivityStatusCode.Error, WarpTelemetry.TruncateMessage(ex.Message, 256));
                    activity?.SetTag(WarpTelemetryAttributes.ErrorType, ex.GetType().FullName);

                    throw;
                }

                if (!moved)
                {
                    break;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            if (string.Equals(status, "succeeded", StringComparison.Ordinal) && cancellationToken.IsCancellationRequested)
            {
                status = "cancelled";
            }

            if (string.Equals(status, "cancelled", StringComparison.Ordinal))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            }

            if (metricsEnabled)
            {
                stopwatch!.Stop();
                WarpTelemetry.MediatorDuration.Record(
                    stopwatch.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("kind", WarpTelemetryAttributes.MediatorKindStream),
                    new KeyValuePair<string, object?>("request_type", requestTypeName),
                    new KeyValuePair<string, object?>("status", status));
                WarpTelemetry.MediatorInFlight.Add(
                    -1,
                    new KeyValuePair<string, object?>("kind", WarpTelemetryAttributes.MediatorKindStream),
                    new KeyValuePair<string, object?>("request_type", requestTypeName));
            }

            activity?.Dispose();

            if (enumerator != null)
            {
                await enumerator.DisposeAsync();
            }
        }
    }
}
