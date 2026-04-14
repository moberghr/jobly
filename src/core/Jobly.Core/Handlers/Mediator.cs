namespace Jobly.Core.Handlers;

public class Mediator : IMediator
{
    private readonly IServiceProvider _provider;

    public Mediator(IServiceProvider provider)
    {
        _provider = provider;
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();

        return JobDispatcher.ExecuteRequestHandler<TResponse>(request, requestType, _provider, cancellationToken);
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();

        return JobDispatcher.ExecuteStreamHandler<TResponse>(request, requestType, _provider, cancellationToken);
    }
}
