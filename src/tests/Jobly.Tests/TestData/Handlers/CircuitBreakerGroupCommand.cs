using Jobly.Core.CircuitBreaker;
using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

public class CircuitBreakerGroupCommand : IJobHandler<CircuitBreakerGroupRequest>
{
    public Task HandleAsync(CircuitBreakerGroupRequest message, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Always fails");
    }
}

[CircuitBreaker(Group = "email-service")]
public class CircuitBreakerGroupRequest : IJob;
