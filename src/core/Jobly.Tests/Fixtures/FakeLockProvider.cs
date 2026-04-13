using System.Collections.Concurrent;
using Medallion.Threading;

namespace Jobly.Tests.Fixtures;

/// <summary>
/// In-memory lock provider that actually tracks held locks.
/// TryAcquire returns null if the lock is already held (real mutex behavior).
/// </summary>
internal class FakeLockProvider : IDistributedLockProvider
{
    private readonly ConcurrentDictionary<string, bool> _heldLocks = new();

    public IDistributedLock CreateLock(string name) => new FakeLock(name, _heldLocks);
}

internal class FakeLock(string name, ConcurrentDictionary<string, bool> heldLocks) : IDistributedLock
{
    public string Name => name;

    public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => TryAcquire(timeout ?? TimeSpan.Zero, cancellationToken) ?? throw new InvalidOperationException("Lock not acquired");

    public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        if (heldLocks.TryAdd(name, true))
        {
            return new FakeHandle(name, heldLocks);
        }

        return null;
    }

    public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var handle = heldLocks.TryAdd(name, true)
            ? new FakeHandle(name, heldLocks)
            : throw new InvalidOperationException("Lock not acquired");

        return new ValueTask<IDistributedSynchronizationHandle>(handle);
    }

    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        IDistributedSynchronizationHandle? handle = heldLocks.TryAdd(name, true)
            ? new FakeHandle(name, heldLocks)
            : null;

        return new ValueTask<IDistributedSynchronizationHandle?>(handle);
    }
}

internal class FakeHandle(string name, ConcurrentDictionary<string, bool> heldLocks) : IDistributedSynchronizationHandle
{
    public CancellationToken HandleLostToken => CancellationToken.None;

    public void Dispose() => heldLocks.TryRemove(name, out _);

    public ValueTask DisposeAsync()
    {
        heldLocks.TryRemove(name, out _);

        return default;
    }
}
