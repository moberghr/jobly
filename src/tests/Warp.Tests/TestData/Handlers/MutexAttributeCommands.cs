using Warp.Core.Concurrency;
using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class MutexAttributeCommand : IJobHandler<MutexAttributeRequest>
{
    public Task HandleAsync(MutexAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[Mutex("static-key")]
public class MutexAttributeRequest : IJob;

public class MutexWaitAttributeCommand : IJobHandler<MutexWaitAttributeRequest>
{
    public Task HandleAsync(MutexWaitAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[Mutex("static-wait-key", Mode = ConcurrencyMode.Wait)]
public class MutexWaitAttributeRequest : IJob;
