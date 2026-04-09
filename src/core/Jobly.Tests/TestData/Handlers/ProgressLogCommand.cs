using Jobly.Core.Handlers;
using Microsoft.Extensions.Logging;

namespace Jobly.Tests.TestData.Handlers;

public class ProgressLogRequest : IJob;

public class ProgressLogCommand(ILogger<ProgressLogCommand> logger) : IJobHandler<ProgressLogRequest>
{
    public async Task HandleAsync(ProgressLogRequest message, CancellationToken ct)
    {
        logger.LogInformation("Step 1 started");
        logger.LogWarning("Step 1 warning");
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
    }
}
