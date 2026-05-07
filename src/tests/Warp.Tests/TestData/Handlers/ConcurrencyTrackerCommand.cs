using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class ConcurrencyTrackerRequest : IJob;

public class ConcurrencyTrackerCommand : IJobHandler<ConcurrencyTrackerRequest>
{
    private readonly ConcurrencyTracker _tracker;

    public ConcurrencyTrackerCommand(ConcurrencyTracker tracker)
    {
        _tracker = tracker;
    }

    public async Task HandleAsync(ConcurrencyTrackerRequest message, CancellationToken cancellationToken)
    {
        _tracker.Enter();
        try
        {
            await Task.Delay(50, cancellationToken);
        }
        finally
        {
            _tracker.Exit();
        }
    }
}

public class ConcurrencyTracker
{
    private int _inFlight;
    private int _maxObserved;
    private int _completed;

    public int MaxObserved => Volatile.Read(ref _maxObserved);

    public int Completed => Volatile.Read(ref _completed);

    public void Enter()
    {
        var current = Interlocked.Increment(ref _inFlight);
        var prevMax = Volatile.Read(ref _maxObserved);
        while (current > prevMax)
        {
            var swapped = Interlocked.CompareExchange(ref _maxObserved, current, prevMax);
            if (swapped == prevMax)
            {
                break;
            }

            prevMax = swapped;
        }
    }

    public void Exit()
    {
        Interlocked.Decrement(ref _inFlight);
        Interlocked.Increment(ref _completed);
    }
}
