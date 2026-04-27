using Microsoft.Extensions.Logging;
using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class LoggingPipelineBehavior : IPipelineBehavior<UnitRequest, Warp.Core.Handlers.Unit>
{
    private readonly ILogger<LoggingPipelineBehavior> _logger;

    public LoggingPipelineBehavior(ILogger<LoggingPipelineBehavior> logger)
    {
        _logger = logger;
    }

    public async Task<Warp.Core.Handlers.Unit> HandleAsync(UnitRequest message, RequestHandlerDelegate<UnitRequest, Warp.Core.Handlers.Unit> next, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Pipeline before handler");
        var result = await next(message, cancellationToken);
        _logger.LogInformation("Pipeline after handler");
        return result;
    }
}
