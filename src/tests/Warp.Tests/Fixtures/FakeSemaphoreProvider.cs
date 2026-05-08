using System.Collections.Concurrent;
using Warp.Core;

namespace Warp.Tests.Fixtures;

/// <summary>
/// In-memory semaphore provider that actually tracks held slots per name.
/// TryAcquire scans slots 0..maxCount-1 and returns the first free one, or null if all are held.
/// </summary>
internal class FakeSemaphoreProvider : IWarpSemaphoreProvider
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> _heldSlots = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(string name, int maxCount, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);

        var slots = _heldSlots.GetOrAdd(name, _ => new ConcurrentDictionary<int, byte>());

        for (var i = 0; i < maxCount; i++)
        {
            if (slots.TryAdd(i, 0))
            {
                IAsyncDisposable handle = new FakeSemaphoreHandle(name, i, _heldSlots);

                return Task.FromResult<IAsyncDisposable?>(handle);
            }
        }

        return Task.FromResult<IAsyncDisposable?>(null);
    }

    /// <summary>
    /// Pre-hold N slots on a name (simulating other workers holding them).
    /// Returns a disposable handle to release all of them.
    /// </summary>
    public IAsyncDisposable HoldSlot(string name, int count = 1)
    {
        var slots = _heldSlots.GetOrAdd(name, _ => new ConcurrentDictionary<int, byte>());
        var taken = new List<int>(count);
        var i = 0;
        while (taken.Count < count)
        {
            if (slots.TryAdd(i, 0))
            {
                taken.Add(i);
            }

            i++;

            if (i > 10_000)
            {
                throw new InvalidOperationException($"Could not pre-hold {count} slots on '{name}'");
            }
        }

        return new FakeMultiSlotHandle(name, taken, _heldSlots);
    }
}

internal class FakeSemaphoreHandle(string name, int slot, ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> heldSlots) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        if (heldSlots.TryGetValue(name, out var slots))
        {
            slots.TryRemove(slot, out _);
        }

        return default;
    }
}

internal class FakeMultiSlotHandle(string name, List<int> slots, ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> heldSlots) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        if (heldSlots.TryGetValue(name, out var bucket))
        {
            foreach (var slot in slots)
            {
                bucket.TryRemove(slot, out _);
            }
        }

        return default;
    }
}
