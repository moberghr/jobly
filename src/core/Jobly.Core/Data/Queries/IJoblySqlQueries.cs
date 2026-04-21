using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Data.Queries;

/// <summary>
/// Provider-specific hand-written SQL for row-locking operations against <see cref="Job"/>.
/// Replaces the legacy <c>RowLockInterceptor</c> — each method returns tracked entities (or in
/// the atomic-claim case, the newly-claimed rows) so callers can continue to compose EF Core
/// state changes + <c>SaveChangesAsync</c> around them.
/// <para>
/// The full-fetch paths (worker + dispatcher) use atomic <c>UPDATE ... RETURNING/OUTPUT</c> so
/// there is no SELECT→UPDATE window. The cold paths (stale recovery, server cleanup, user
/// commands) use <c>SELECT ... FOR UPDATE [SKIP LOCKED]</c> inside the caller's transaction,
/// followed by the caller's own EF-tracked updates.
/// </para>
/// </summary>
public interface IJoblySqlQueries<TContext>
    where TContext : DbContext
{
    /// <summary>
    /// Atomically claims up to <paramref name="limit"/> rows in <c>State=Enqueued</c> matching
    /// any of <paramref name="queues"/>, flipping them to <c>State=Processing</c> and stamping
    /// the worker / timestamp. Uses <c>FOR UPDATE SKIP LOCKED</c> (PG) or
    /// <c>WITH (ROWLOCK, UPDLOCK, READPAST)</c> (SQL Server) so concurrent workers get distinct
    /// rows without contention. Returns the post-update row as a tracked entity.
    /// </summary>
    Task<List<Job>> ClaimEnqueuedJobsAsync(
        TContext context,
        string[] queues,
        Guid workerId,
        DateTime now,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// Locks the next <c>Kind=Message</c> row in <c>State=Enqueued</c> with SKIP LOCKED semantics
    /// and returns it as a tracked entity. Caller runs inside a transaction, mutates the entity
    /// + adds child jobs, then SaveChanges to commit atomically.
    /// </summary>
    Task<Job?> LockNextEnqueuedMessageAsync(TContext context, CancellationToken ct);

    /// <summary>
    /// Locks <c>Kind=Job</c> rows in <c>State=Processing</c> whose <c>LastKeepAlive</c> is older
    /// than <paramref name="cutoff"/> with SKIP LOCKED semantics. Returns tracked entities for
    /// the caller to transition to Enqueued / Failed / Deleted per its recovery policy.
    /// </summary>
    Task<List<Job>> LockStaleProcessingJobsAsync(
        TContext context,
        DateTime cutoff,
        CancellationToken ct);

    /// <summary>
    /// Locks a single job by ID with SKIP LOCKED semantics. Used by user-initiated commands
    /// (DeleteJob, RequeueJob) that should abort if the row is currently being processed by a
    /// worker or concurrent command.
    /// </summary>
    Task<Job?> LockJobByIdAsync(TContext context, Guid jobId, CancellationToken ct);

    /// <summary>
    /// Locks a single job by ID with a blocking lock (WAIT). Used by RequeueJob when it needs
    /// to update the parent row and must serialize against concurrent parent writers.
    /// </summary>
    Task<Job?> LockJobByIdWaitAsync(TContext context, Guid jobId, CancellationToken ct);

    /// <summary>
    /// Locks every <see cref="Server"/> row with a blocking lock (WAIT — no SKIP LOCKED).
    /// Used by server cleanup to freeze the set while it decides which servers are stale.
    /// The blocking lock serializes against <c>Heartbeat</c>'s per-row update so we can't
    /// delete a server that just sent a fresh heartbeat.
    /// </summary>
    Task<List<Server>> LockAllServersAsync(TContext context, CancellationToken ct);
}
