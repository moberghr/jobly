using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

// 200ms handler that respects cancellation. Lets shutdown tests have work truly
// in-flight when the server is disposed, without the 30s tail of CancellableRequest.
public class ShortDelayRequest : IJob;

public class ShortDelayCommand : IJobHandler<ShortDelayRequest>
{
    public async Task HandleAsync(ShortDelayRequest message, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
    }
}
