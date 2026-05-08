using System.Collections.Concurrent;
using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class ConcurrencyTrackerRequest : IJob
{
    public string Key { get; set; } = string.Empty;
}

public class ConcurrencyTrackerCommand : IJobHandler<ConcurrencyTrackerRequest>
{
    private readonly ConcurrencyTracker _tracker;

    public ConcurrencyTrackerCommand(ConcurrencyTracker tracker)
    {
        _tracker = tracker;
    }

    public async Task HandleAsync(ConcurrencyTrackerRequest message, CancellationToken cancellationToken)
    {
        _tracker.Enter(message.Key);
        try
        {
            await Task.Delay(50, cancellationToken);
        }
        finally
        {
            _tracker.Exit(message.Key);
        }
    }
}

public class ConcurrencyTracker
{
    private int _inFlight;
    private int _maxObserved;
    private int _completed;
    private readonly ConcurrentDictionary<string, KeyState> _byKey = new();

    public int MaxObserved => Volatile.Read(ref _maxObserved);

    public int Completed => Volatile.Read(ref _completed);

    public int MaxObservedFor(string key) =>
        _byKey.TryGetValue(key, out var s) ? Volatile.Read(ref s.Max) : 0;

    public int CompletedFor(string key) =>
        _byKey.TryGetValue(key, out var s) ? Volatile.Read(ref s.Completed) : 0;

    public void Enter(string key)
    {
        BumpMax(Interlocked.Increment(ref _inFlight), ref _maxObserved);

        var state = _byKey.GetOrAdd(key, _ => new KeyState());
        BumpMax(Interlocked.Increment(ref state.InFlight), ref state.Max);
    }

    public void Exit(string key)
    {
        Interlocked.Decrement(ref _inFlight);
        Interlocked.Increment(ref _completed);

        if (_byKey.TryGetValue(key, out var state))
        {
            Interlocked.Decrement(ref state.InFlight);
            Interlocked.Increment(ref state.Completed);
        }
    }

    private static void BumpMax(int current, ref int max)
    {
        var prevMax = Volatile.Read(ref max);
        while (current > prevMax)
        {
            var swapped = Interlocked.CompareExchange(ref max, current, prevMax);
            if (swapped == prevMax)
            {
                break;
            }

            prevMax = swapped;
        }
    }

    private sealed class KeyState
    {
        public int InFlight;
        public int Max;
        public int Completed;
    }
}
