using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

/// <summary>
/// Handler that writes a key to metadata during execution.
/// After completion, the test can verify this key was persisted.
/// </summary>
public class MetadataWriterRequest : IJob;

public class MetadataWriterHandler(IJobContext ctx) : IJobHandler<MetadataWriterRequest>
{
    public Task HandleAsync(MetadataWriterRequest message, CancellationToken cancellationToken)
    {
        ctx.Metadata["HandlerWrote"] = "from-handler";

        return Task.CompletedTask;
    }
}
