using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class ThrowExceptionCommand : IJobHandler<ThrowExceptionRequest>
{
    public async Task HandleAsync(ThrowExceptionRequest message, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

public class ThrowExceptionRequest : IJob;
