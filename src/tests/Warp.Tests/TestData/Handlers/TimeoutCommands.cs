using Warp.Core.Handlers;
using Warp.Core.Timeout;

namespace Warp.Tests.TestData.Handlers;

[Timeout(seconds: 30)]
public class TimeoutAttributeRequest : IJob;

[Timeout(seconds: 60, Mode = TimeoutMode.Fail)]
public class TimeoutFailModeRequest : IJob;

[Timeout(seconds: 60, Mode = TimeoutMode.Fail, Scope = TimeoutScope.Total)]
public class TimeoutTotalScopeRequest : IJob;

[Timeout(seconds: 99)]
public abstract class TimeoutBaseRequest : IJob;

public class TimeoutDerivedWithoutAttributeRequest : TimeoutBaseRequest;

public class TimeoutDerivedWithoutAttributeCommand : IJobHandler<TimeoutDerivedWithoutAttributeRequest>
{
    public Task HandleAsync(TimeoutDerivedWithoutAttributeRequest message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public class TimeoutAttributeCommand : IJobHandler<TimeoutAttributeRequest>
{
    public Task HandleAsync(TimeoutAttributeRequest message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public class TimeoutFailModeCommand : IJobHandler<TimeoutFailModeRequest>
{
    public Task HandleAsync(TimeoutFailModeRequest message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public class TimeoutTotalScopeCommand : IJobHandler<TimeoutTotalScopeRequest>
{
    public Task HandleAsync(TimeoutTotalScopeRequest message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
