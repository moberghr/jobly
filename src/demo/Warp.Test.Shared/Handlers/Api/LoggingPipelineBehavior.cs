using Microsoft.Extensions.Logging;
using Warp.Core.Handlers;

namespace Warp.Core.Handlers;

public class TimingPipelineBehavior<T, TResponse> : IPipelineBehavior<T, TResponse>
    where T : IRequest<TResponse>
{
    private readonly ILogger<TimingPipelineBehavior<T, TResponse>> _logger;

    public TimingPipelineBehavior(ILogger<TimingPipelineBehavior<T, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> HandleAsync(T message, RequestHandlerDelegate<T, TResponse> next, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        _logger.LogInformation("Starting handler for {Type}", typeof(T).Name);
        var result = await next(message, cancellationToken);
        var elapsed = DateTime.UtcNow - start;
        _logger.LogInformation("Completed handler for {Type} in {Elapsed}ms", typeof(T).Name, elapsed.TotalMilliseconds);
        return result;
    }
}
