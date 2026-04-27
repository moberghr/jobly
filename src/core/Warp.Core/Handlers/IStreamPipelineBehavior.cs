using System.Diagnostics.CodeAnalysis;

namespace Warp.Core.Handlers;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Public API")]
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<in TRequest, out TResponse>(TRequest request, CancellationToken cancellationToken)
    where TRequest : IStreamRequest<TResponse>;

/// <summary>
/// Pipeline behavior that wraps handler execution for stream request types.
/// Call the <c>next</c> delegate to continue the pipeline.
/// </summary>
public interface IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Public API")]
    IAsyncEnumerable<TResponse> HandleAsync(TRequest request, StreamHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken);
}
