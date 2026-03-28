using Jobly.Core.Handlers;
using Microsoft.Extensions.Logging;

namespace Jobly.Tests.TestData.Handlers;

public class LoggingPipelineBehavior : IPipelineBehavior<UnitRequest>
{
    private readonly ILogger<LoggingPipelineBehavior> _logger;

    public LoggingPipelineBehavior(ILogger<LoggingPipelineBehavior> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(UnitRequest message, JobHandlerDelegate next, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Pipeline before handler");
        await next();
        _logger.LogInformation("Pipeline after handler");
    }
}
