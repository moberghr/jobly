using System.Diagnostics.CodeAnalysis;

namespace Jobly.Core.Handlers;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Public API")]
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Pipeline behavior that wraps handler execution for all request types (IJob, IMessage, IRequest).
/// Call <paramref name="next"/> to continue the pipeline.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Public API")]
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
