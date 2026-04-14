namespace Jobly.Core.Handlers;

public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
