namespace Jobly.Core.Handlers;

public interface IStreamRequest<out TResponse> : IRequest<IAsyncEnumerable<TResponse>>;
