using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Warp.Core.Data.Entities;

namespace Warp.Tests.Fixtures;

/// <summary>
/// EF SaveChanges interceptor that lets tests await a specific JobLog row insertion
/// deterministically. Replaces 200 ms polling in <c>WarpTestServer.WaitForJobLog</c> —
/// the waiter completes the instant the worker's SaveChanges commits a matching row,
/// not up to one poll cycle later. Registered once per <see cref="WarpTestServer"/>
/// instance so every DbContext built from that server's options invokes it.
/// <para>
/// Subscriptions are matched by <c>(JobId, EventType)</c>. Each subscription is
/// single-shot: once the matching insert fires it completes its TCS and unregisters.
/// Subscribers that never match are unregistered when their CancellationToken trips
/// (typically the test's overall deadline).
/// </para>
/// </summary>
internal sealed class JobLogObserver : SaveChangesInterceptor
{
    // Pending insertions snapshot per in-flight SaveChanges. Keyed on the DbContext
    // instance because both SavingChangesAsync and SavedChangesAsync receive the same
    // context, and DbContext.SaveChanges is single-threaded per instance. AsyncLocal
    // does NOT work here: EF Core invokes interceptors inside a captured
    // ExecutionContext, so any Set inside SavingChangesAsync is invisible to
    // SavedChangesAsync.
    private readonly ConcurrentDictionary<DbContext, List<(Guid JobId, string EventType)>> _pending = new();

    private readonly ConcurrentBag<Waiter> _waiters = [];

    public Subscription Subscribe(Guid jobId, string eventType)
    {
        var waiter = new Waiter(jobId, eventType);
        _waiters.Add(waiter);

        return new Subscription(waiter, this);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        // Snapshot Added JobLog entries BEFORE SaveChanges so we still see the right
        // (JobId, EventType) values — once persisted, ChangeTracker entries become
        // Unchanged and the "just added" signal is lost.
        if (eventData.Context == null)
        {
            return new(result);
        }

        var added = eventData.Context
            .ChangeTracker
            .Entries<JobLog>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => (e.Entity.JobId, e.Entity.EventType))
            .ToList();

        if (added.Count > 0)
        {
            _pending[eventData.Context] = added;
        }

        return new(result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context == null || !_pending.TryRemove(eventData.Context, out var snapshot))
        {
            return new(result);
        }

        foreach (var (jobId, eventType) in snapshot)
        {
            foreach (var waiter in _waiters)
            {
                if (waiter.Completed)
                {
                    continue;
                }

                if (waiter.JobId == jobId
                    && string.Equals(waiter.EventType, eventType, StringComparison.Ordinal))
                {
                    waiter.Complete();
                }
            }
        }

        return new(result);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        // Drop the snapshot on rollback — nothing was committed, no waiter should fire.
        if (eventData.Context != null)
        {
            _pending.TryRemove(eventData.Context, out _);
        }

        return Task.CompletedTask;
    }

    internal sealed class Waiter
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Waiter(Guid jobId, string eventType)
        {
            JobId = jobId;
            EventType = eventType;
        }

        public Guid JobId { get; }

        public string EventType { get; }

        public bool Completed { get; private set; }

        public Task Task => _tcs.Task;

        public void Complete()
        {
            Completed = true;
            _tcs.TrySetResult();
        }

        public void Cancel(CancellationToken ct)
        {
            Completed = true;
            _tcs.TrySetCanceled(ct);
        }
    }

    /// <summary>
    /// IDisposable subscription handle — disposing marks the waiter completed (so it
    /// won't fire spuriously). Lazy cleanup: ConcurrentBag has no remove, and completed
    /// waiters short-circuit via the Completed flag on the next interceptor pass.
    /// </summary>
    internal sealed class Subscription : IDisposable
    {
        private readonly Waiter _waiter;

        public Subscription(Waiter waiter, JobLogObserver owner)
        {
            _waiter = waiter;
            _ = owner;
        }

        public Task Task => _waiter.Task;

        public void Dispose()
        {
            if (!_waiter.Completed)
            {
                _waiter.Cancel(CancellationToken.None);
            }
        }
    }
}
