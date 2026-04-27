using System.Collections.Concurrent;
using Warp.Core;

namespace Warp.Tests.Fixtures;

/// <summary>
/// In-memory lock provider that actually tracks held locks.
/// TryAcquire returns null if the lock is already held (real mutex behavior).
/// </summary>
internal class FakeLockProvider : IWarpLockProvider
{
    private readonly ConcurrentDictionary<string, bool> _heldLocks = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        if (_heldLocks.TryAdd(name, true))
        {
            IAsyncDisposable handle = new FakeHandle(name, _heldLocks);

            return Task.FromResult<IAsyncDisposable?>(handle);
        }

        return Task.FromResult<IAsyncDisposable?>(null);
    }

    /// <summary>
    /// Pre-hold a lock (simulating another worker holding it).
    /// Returns a disposable handle to release the lock.
    /// </summary>
    public IAsyncDisposable HoldLock(string name)
    {
        if (!_heldLocks.TryAdd(name, true))
        {
            throw new InvalidOperationException($"Lock '{name}' is already held");
        }

        return new FakeHandle(name, _heldLocks);
    }
}

internal class FakeHandle(string name, ConcurrentDictionary<string, bool> heldLocks) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        heldLocks.TryRemove(name, out _);

        return default;
    }
}
