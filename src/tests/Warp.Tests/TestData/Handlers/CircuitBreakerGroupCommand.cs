using Warp.Core.CircuitBreaker;
using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class CircuitBreakerGroupCommand : IJobHandler<CircuitBreakerGroupRequest>
{
    public Task HandleAsync(CircuitBreakerGroupRequest message, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Always fails");
    }
}

[CircuitBreaker(Group = "email-service")]
public class CircuitBreakerGroupRequest : IJob;
