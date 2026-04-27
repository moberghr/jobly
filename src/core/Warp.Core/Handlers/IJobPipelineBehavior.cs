using System.Diagnostics.CodeAnalysis;

namespace Warp.Core.Handlers;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Public API")]
public delegate Task<TResponse> RequestHandlerDelegate<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken)
    where TRequest : IRequest<TResponse>;

/// <summary>
/// Pipeline behavior that wraps handler execution for all request types (IJob, IMessage, IRequest).
/// Call the <c>next</c> delegate to continue the pipeline.
/// </summary>
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Public API")]
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken);
}
