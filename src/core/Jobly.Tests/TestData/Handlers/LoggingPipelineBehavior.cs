using Jobly.Core.Handlers;
using Microsoft.Extensions.Logging;

namespace Jobly.Tests.TestData.Handlers;

public class LoggingPipelineBehavior : IPipelineBehavior<UnitRequest, Jobly.Core.Handlers.Unit>
{
    private readonly ILogger<LoggingPipelineBehavior> _logger;

    public LoggingPipelineBehavior(ILogger<LoggingPipelineBehavior> logger)
    {
        _logger = logger;
    }

    public async Task<Jobly.Core.Handlers.Unit> HandleAsync(UnitRequest message, RequestHandlerDelegate<UnitRequest, Jobly.Core.Handlers.Unit> next, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Pipeline before handler");
        var result = await next(message, cancellationToken);
        _logger.LogInformation("Pipeline after handler");
        return result;
    }
}
