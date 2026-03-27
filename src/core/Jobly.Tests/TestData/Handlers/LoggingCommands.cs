using Jobly.Core.Handlers;
using Microsoft.Extensions.Logging;

namespace Jobly.Tests.TestData.Handlers;

public class LoggingRequest : IJob { }

public class LoggingCommand : IJobHandler<LoggingRequest>
{
    private readonly ILogger<LoggingCommand> _logger;

    public LoggingCommand(ILogger<LoggingCommand> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(LoggingRequest message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing logging request");
        _logger.LogWarning("This is a warning");
        return Task.CompletedTask;
    }
}

public class ErrorLoggingRequest : IJob { }

public class ErrorLoggingCommand : IJobHandler<ErrorLoggingRequest>
{
    private readonly ILogger<ErrorLoggingCommand> _logger;

    public ErrorLoggingCommand(ILogger<ErrorLoggingCommand> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(ErrorLoggingRequest message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("About to fail");
        throw new InvalidOperationException("Test error from handler");
    }
}
