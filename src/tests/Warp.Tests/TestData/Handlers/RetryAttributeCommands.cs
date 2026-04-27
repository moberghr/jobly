using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

// Handler with [Retry] attribute
[Retry(5)]
public class RetryAttributeHandlerCommand : IJobHandler<RetryAttributeHandlerRequest>
{
    public Task HandleAsync(RetryAttributeHandlerRequest message, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Always fails");
    }
}

public class RetryAttributeHandlerRequest : IJob;

// Job class with [Retry] attribute, handler without attribute
public class RetryAttributeJobCommand : IJobHandler<RetryAttributeJobRequest>
{
    public Task HandleAsync(RetryAttributeJobRequest message, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Always fails");
    }
}

[Retry(4)]
public class RetryAttributeJobRequest : IJob;

// Both handler and job have [Retry] — handler should win
[Retry(7)]
public class RetryAttributeBothCommand : IJobHandler<RetryAttributeBothRequest>
{
    public Task HandleAsync(RetryAttributeBothRequest message, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Always fails");
    }
}

[Retry(2)]
public class RetryAttributeBothRequest : IJob;

// Handler with [Retry] that includes custom delays
[Retry(3, Delays = [100, 200, 300])]
public class RetryAttributeWithDelaysCommand : IJobHandler<RetryAttributeWithDelaysRequest>
{
    public Task HandleAsync(RetryAttributeWithDelaysRequest message, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Always fails");
    }
}

public class RetryAttributeWithDelaysRequest : IJob;
