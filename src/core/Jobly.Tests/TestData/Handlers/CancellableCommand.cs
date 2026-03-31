using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

public class CancellableRequest : IJob;

public class CancellableCommand : IJobHandler<CancellableRequest>
{
    public async Task HandleAsync(CancellableRequest message, CancellationToken cancellationToken)
    {
        // Simulate long-running work that respects cancellation
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
    }
}
