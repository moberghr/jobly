using Jobly.Core.Handlers;
using Jobly.Core.Mutex;

namespace Jobly.Tests.TestData.Handlers;

public class MutexAttributeCommand : IJobHandler<MutexAttributeRequest>
{
    public Task HandleAsync(MutexAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[Mutex("static-key")]
public class MutexAttributeRequest : IJob;
