using Jobly.Core;
using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

public class SpawnChildJobRequest : IJob;

public class SpawnChildJobHandler : IJobHandler<SpawnChildJobRequest>
{
    private readonly IPublisher _publisher;

    public SpawnChildJobHandler(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task HandleAsync(SpawnChildJobRequest message, CancellationToken cancellationToken)
    {
        // Spawn a child job during execution — should inherit trace
        await _publisher.Enqueue(new UnitRequest());
    }
}

/// <summary>
/// Handler that spawns another SpawnChildJobRequest, creating a 3-level trace chain.
/// </summary>
public class SpawnGrandchildJobRequest : IJob;

public class SpawnGrandchildJobHandler : IJobHandler<SpawnGrandchildJobRequest>
{
    private readonly IPublisher _publisher;

    public SpawnGrandchildJobHandler(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task HandleAsync(SpawnGrandchildJobRequest message, CancellationToken cancellationToken)
    {
        await _publisher.Enqueue(new SpawnChildJobRequest());
    }
}
