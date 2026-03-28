using Jobly.Core;
using Jobly.Core.Entities;
using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

/// <summary>
/// Handler that creates a batch of 3 UnitRequests with a continuation UnitRequest.
/// Tests: batch creation inside handler, trace propagation to batch jobs + continuation.
/// </summary>
public class SpawnBatchRequest : IJob { }

public class SpawnBatchHandler : IJobHandler<SpawnBatchRequest>
{
    private readonly IBatchPublisher _batchPublisher;

    public SpawnBatchHandler(IBatchPublisher batchPublisher)
    {
        _batchPublisher = batchPublisher;
    }

    public async Task HandleAsync(SpawnBatchRequest message, CancellationToken cancellationToken)
    {
        var jobs = new List<UnitRequest> { new(), new(), new() };
        var batchId = await _batchPublisher.StartNew(jobs);
        await _batchPublisher.ContinueBatchWith(new List<UnitRequest> { new() }, batchId);
    }
}
