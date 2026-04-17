using Jobly.Core.Handlers;
using Jobly.Core.NoRestart;

namespace Jobly.Tests.TestData.Handlers;

[NoRestart]
public class NoRestartAttributeRequest : IJob;

[Restart]
public class RestartAttributeRequest : IJob;

[NoRestart]
[Restart]
public class BothAttributesRequest : IJob;

public class NoRestartAttributeCommand : IJobHandler<NoRestartAttributeRequest>
{
    public Task HandleAsync(NoRestartAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public class RestartAttributeCommand : IJobHandler<RestartAttributeRequest>
{
    public Task HandleAsync(RestartAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public class BothAttributesCommand : IJobHandler<BothAttributesRequest>
{
    public Task HandleAsync(BothAttributesRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
