using Warp.Core;
using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class SpawnChildJobRequest : IJob;

public class SpawnChildJobHandler : IJobHandler<SpawnChildJobRequest>
{
    private readonly IPublisher _publisher;
    private readonly TestContext _context;

    public SpawnChildJobHandler(IPublisher publisher, TestContext context)
    {
        _publisher = publisher;
        _context = context;
    }

    public async Task HandleAsync(SpawnChildJobRequest message, CancellationToken cancellationToken)
    {
        // Spawn a child job during execution — should inherit trace
        await _publisher.Enqueue(new UnitRequest());
        await _context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Handler that spawns another SpawnChildJobRequest, creating a 3-level trace chain.
/// </summary>
public class SpawnGrandchildJobRequest : IJob;

public class SpawnGrandchildJobHandler : IJobHandler<SpawnGrandchildJobRequest>
{
    private readonly IPublisher _publisher;
    private readonly TestContext _context;

    public SpawnGrandchildJobHandler(IPublisher publisher, TestContext context)
    {
        _publisher = publisher;
        _context = context;
    }

    public async Task HandleAsync(SpawnGrandchildJobRequest message, CancellationToken cancellationToken)
    {
        await _publisher.Enqueue(new SpawnChildJobRequest());
        await _context.SaveChangesAsync(cancellationToken);
    }
}
