using Jobly.Core.Handlers;

namespace Jobly.Core.Handlers;

public class SlowRequest : IJob;

public class SlowCommand : IJobHandler<SlowRequest>
{
    public async Task HandleAsync(SlowRequest message, CancellationToken cancellationToken)
    {
        await Task.Delay(30000, cancellationToken); // 30 seconds
    }
}
