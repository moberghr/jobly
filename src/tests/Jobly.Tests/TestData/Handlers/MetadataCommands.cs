using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

public class MetadataRequest : IJob;

public class MetadataCommand(IJobContext jobContext, MetadataCapture capture) : IJobHandler<MetadataRequest>
{
    public Task HandleAsync(MetadataRequest message, CancellationToken cancellationToken)
    {
        capture.CapturedMetadata = new Dictionary<string, object>(jobContext.Metadata);
        return Task.CompletedTask;
    }
}

public class MetadataCapture
{
    public Dictionary<string, object>? CapturedMetadata { get; set; }
}

public class TestMetadataPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        context.Metadata["test-key"] = "test-value";
        context.Metadata["source"] = "publish-pipeline";
        return next();
    }
}
