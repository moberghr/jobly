using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Warp.Core.Data.Entities;

namespace Warp.Tests.Fixtures;

/// <summary>
/// EF SaveChanges + transaction interceptor that lets tests await a specific JobLog
/// row insertion deterministically. Replaces the 200 ms polling that used to live in
/// <c>WarpTestServer.WaitForJobLog</c> — the waiter completes the instant the row is
/// visible to other connections, not up to one poll cycle later. Registered once per
/// <see cref="WarpTestServer"/> instance so every DbContext built from that server's
/// options invokes it.
/// <para>
/// Subscriptions are matched by <c>(JobId, EventType)</c>. Each subscription is
/// single-shot: once the matching insert is visible it completes its TCS and
/// unregisters. Subscribers that never match are unregistered when their
/// Subscription is disposed (typically end of the test).
/// </para>
/// <para>
/// Transaction-aware: when <c>SaveChangesAsync</c> runs inside an explicit
/// <c>BeginTransactionAsync</c> scope (worker's cancel/complete/fail paths, see
/// <c>WarpWorkerService.cs:277</c>), the row is staged but not yet committed when
/// <c>SavedChangesAsync</c> fires. Firing the waiter there would let the test race
/// past the commit and read the prior state under <c>READ COMMITTED</c>. So the
/// snapshot is parked keyed on the underlying <see cref="DbTransaction"/> and only
/// fired once <see cref="TransactionCommittedAsync"/> reports the commit landed.
/// </para>
/// </summary>
internal sealed class JobLogObserver : SaveChangesInterceptor, IDbTransactionInterceptor
{
    // Pending insertions snapshot per in-flight SaveChanges. Keyed on the DbContext
    // instance because both SavingChangesAsync and SavedChangesAsync receive the same
    // context, and DbContext.SaveChanges is single-threaded per instance. AsyncLocal
    // does NOT work here: EF Core invokes interceptors inside a captured
    // ExecutionContext, so any Set inside SavingChangesAsync is invisible to
    // SavedChangesAsync.
    private readonly ConcurrentDictionary<DbContext, List<(Guid JobId, string EventType)>> _pendingByContext = new();

    // Snapshots that landed inside an explicit DB transaction. We hold them here until
    // the transaction commits — firing on SavedChangesAsync would beat the commit on a
    // loaded runner and the test's next read sees the pre-commit state.
    private readonly ConcurrentDictionary<DbTransaction, List<(Guid JobId, string EventType)>> _pendingByTransaction = new();

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
            _pendingByContext[eventData.Context] = added;
        }

        return new(result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context == null || !_pendingByContext.TryRemove(eventData.Context, out var snapshot))
        {
            return new(result);
        }

        // No explicit transaction: SavedChangesAsync runs after the implicit batch
        // transaction has already committed at the database, so the row is visible
        // to other connections — fire immediately.
        var dbTransaction = eventData.Context.Database.CurrentTransaction?.GetDbTransaction();
        if (dbTransaction == null)
        {
            FireWaiters(snapshot);

            return new(result);
        }

        // Inside an explicit transaction: park the snapshot, fire on commit.
        _pendingByTransaction.AddOrUpdate(
            dbTransaction,
            _ => snapshot,
            (_, existing) =>
            {
                existing.AddRange(snapshot);

                return existing;
            });

        return new(result);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        // Drop the snapshot on rollback — nothing was committed, no waiter should fire.
        if (eventData.Context != null)
        {
            _pendingByContext.TryRemove(eventData.Context, out _);
        }

        return Task.CompletedTask;
    }

    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (_pendingByTransaction.TryRemove(transaction, out var snapshot))
        {
            FireWaiters(snapshot);
        }

        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (_pendingByTransaction.TryRemove(transaction, out var snapshot))
        {
            FireWaiters(snapshot);
        }
    }

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _pendingByTransaction.TryRemove(transaction, out _);

        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        _pendingByTransaction.TryRemove(transaction, out _);
    }

    public Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _pendingByTransaction.TryRemove(transaction, out _);

        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        _pendingByTransaction.TryRemove(transaction, out _);
    }

    private void FireWaiters(List<(Guid JobId, string EventType)> snapshot)
    {
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
