using Warp.Core.Concurrency;
using Warp.Core.Handlers;
using Warp.Core.RateLimit;

namespace Warp.Tests.TestData.Handlers;

public class RateLimitAttributeCommand : IJobHandler<RateLimitAttributeRequest>
{
    public Task HandleAsync(RateLimitAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[RateLimit("rl-static", count: 2, perSeconds: 60)]
public class RateLimitAttributeRequest : IJob;

public class RateLimitWaitAttributeCommand : IJobHandler<RateLimitWaitAttributeRequest>
{
    public Task HandleAsync(RateLimitWaitAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[RateLimit("rl-wait", count: 2, perSeconds: 60, Mode = RateLimitMode.Wait)]
public class RateLimitWaitAttributeRequest : IJob;

public class RateLimitSlidingAttributeCommand : IJobHandler<RateLimitSlidingAttributeRequest>
{
    public Task HandleAsync(RateLimitSlidingAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[RateLimit("rl-sliding", count: 2, perSeconds: 60, Style = RateLimitStyle.Sliding)]
public class RateLimitSlidingAttributeRequest : IJob;

public class MutexAndRateLimitCommand : IJobHandler<MutexAndRateLimitRequest>
{
    public Task HandleAsync(MutexAndRateLimitRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[Mutex("rl-combo-mutex")]
[RateLimit("rl-combo-rate", count: 10, perSeconds: 60)]
public class MutexAndRateLimitRequest : IJob;
