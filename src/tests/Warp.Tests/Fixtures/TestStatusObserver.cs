using System.Collections.Concurrent;
using Warp.Core.BackgroundServices;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Test implementation of <see cref="IBackgroundServiceStatusObserver"/>. Lets a test
/// register a <see cref="TaskCompletionSource"/> for an upcoming (name, status) transition
/// BEFORE the action that triggers it, eliminating the polling races that an after-the-fact
/// observer pattern would have.
/// </summary>
public sealed class TestStatusObserver : IBackgroundServiceStatusObserver
{
    private readonly Lock _lock = new();
    private readonly List<(string Name, BackgroundServiceStatus Status, TaskCompletionSource Tcs)> _waiters = [];

    /// <summary>
    /// Register interest in the NEXT occurrence of <paramref name="serviceName"/> reaching
    /// <paramref name="status"/>. Returns a task that completes when the matching transition
    /// fires. Call BEFORE triggering the action that should cause the transition.
    /// </summary>
    public Task NextStatus(string serviceName, BackgroundServiceStatus status)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _waiters.Add((serviceName, status, tcs));
        }

        return tcs.Task;
    }

    public void OnStatusChanged(string serviceName, BackgroundServiceStatus status)
    {
        TaskCompletionSource? matched = null;
        lock (_lock)
        {
            for (var i = 0; i < _waiters.Count; i++)
            {
                var waiter = _waiters[i];
                if (string.Equals(waiter.Name, serviceName, StringComparison.Ordinal) && waiter.Status == status)
                {
                    matched = waiter.Tcs;
                    _waiters.RemoveAt(i);
                    break;
                }
            }
        }

        matched?.TrySetResult();
    }
}
