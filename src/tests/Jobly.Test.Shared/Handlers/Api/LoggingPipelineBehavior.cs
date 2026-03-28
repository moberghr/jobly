using Jobly.Core.Handlers;
using Microsoft.Extensions.Logging;

namespace Jobly.Core.Handlers;

public class TimingPipelineBehavior<T> : IPipelineBehavior<T> where T : class
{
    private readonly ILogger<TimingPipelineBehavior<T>> _logger;

    public TimingPipelineBehavior(ILogger<TimingPipelineBehavior<T>> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        _logger.LogInformation("Starting handler for {Type}", typeof(T).Name);
        await next();
        var elapsed = DateTime.UtcNow - start;
        _logger.LogInformation("Completed handler for {Type} in {Elapsed}ms", typeof(T).Name, elapsed.TotalMilliseconds);
    }
}
