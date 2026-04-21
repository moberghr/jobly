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

[NoRestart]
public abstract class NoRestartBaseRequest : IJob;

public class DerivedFromNoRestartBaseRequest : NoRestartBaseRequest;

public class DerivedFromNoRestartBaseCommand : IJobHandler<DerivedFromNoRestartBaseRequest>
{
    public Task HandleAsync(DerivedFromNoRestartBaseRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[Restart]
public abstract class RestartBaseRequest : IJob;

[NoRestart]
public class DerivedOverridesBaseRestartRequest : RestartBaseRequest;

public class DerivedOverridesBaseRestartCommand : IJobHandler<DerivedOverridesBaseRestartRequest>
{
    public Task HandleAsync(DerivedOverridesBaseRestartRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

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
