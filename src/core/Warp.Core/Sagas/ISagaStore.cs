namespace Warp.Core.Sagas;

/// <summary>
/// Persists saga instances. The store is the only thing in the saga subsystem that touches
/// the database directly — everything else (proxy, handlers, attributes) sits above it.
/// </summary>
/// <remarks>
/// Cross-process serialization is provided upstream by <c>SagaHandlerProxy</c> via a mutex on
/// <c>warp:saga:{TSaga.FullName}:{CorrelationKey}</c>. Defense-in-depth optimistic concurrency
/// is provided by the existing <c>SaveChangesConcurrencyTokenInterceptor</c> bumping
/// <c>Saga.Version</c> on every save.
/// </remarks>
public interface ISagaStore
{
    /// <summary>
    /// Loads the live saga for <paramref name="correlationKey"/>, or <c>null</c> if none exists.
    /// The returned instance is fully-typed (<typeparamref name="TSaga"/>) and tracked by the
    /// underlying DbContext so subsequent <c>Update</c> calls round-trip through change tracking.
    /// </summary>
    Task<TSaga?> Load<TSaga>(string correlationKey, CancellationToken cancellationToken)
        where TSaga : Saga, new();

    /// <summary>
    /// Inserts a new saga. Serializes the typed instance to <c>StateJson</c> and stamps
    /// <c>CreatedAt</c>/<c>UpdatedAt</c>. The row is not visible to other transactions until
    /// the caller invokes <c>SaveChangesAsync</c>.
    /// </summary>
    void Add<TSaga>(TSaga saga)
        where TSaga : Saga;

    /// <summary>
    /// Updates the persisted row from the in-memory saga: serializes state to <c>StateJson</c>
    /// and bumps <c>UpdatedAt</c>. The <c>Version</c> token is bumped by the SaveChanges
    /// interceptor automatically.
    /// </summary>
    void Update<TSaga>(TSaga saga)
        where TSaga : Saga;

    /// <summary>
    /// Marks the persisted row for deletion. Used when the saga's handler calls
    /// <c>MarkCompleted()</c> — completion in this model is row removal, mirroring Wolverine.
    /// </summary>
    void Remove<TSaga>(TSaga saga)
        where TSaga : Saga;

    /// <summary>
    /// Commits all pending Add/Update/Remove operations to the database and fires push
    /// notifications for any <c>JobEnqueued</c> / <c>MessageEnqueued</c> rows the saga handler
    /// added via <c>IPublisher.Publish</c> / <c>Enqueue</c>. On
    /// On a <c>DbUpdateConcurrencyException</c> the implementation
    /// clears the change tracker before rethrowing, so the worker's outbox does not re-commit the
    /// conflicting saga row.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Drops every pending change in the change tracker. Used by the proxy to roll back
    /// uncommitted state when the saga handler throws — both the saga's own Modified/Added entry
    /// and any child rows from <c>IPublisher.Publish</c> inside the handler.
    /// </summary>
    void DiscardPendingChanges();

    /// <summary>
    /// Records that <paramref name="jobId"/> ran for <paramref name="sagaId"/>. Powers the
    /// dashboard's activity log query. One row per handler invocation; cheap to write.
    /// </summary>
    void RecordJobLink(Guid sagaId, Guid jobId);

    /// <summary>
    /// Stages deletion of every <c>SagaJobLink</c> row for <paramref name="sagaId"/>. Called by
    /// the proxy when the saga is being completed (<c>MarkCompleted</c>) — link rows are only
    /// useful for live sagas, so they get cleaned up alongside the <c>SagaState</c> row.
    /// </summary>
    /// <remarks>
    /// Loads the existing links into the change tracker and stages their removal so they commit
    /// atomically with the saga row removal inside the proxy's <c>SaveChangesAsync</c>. If the
    /// saga save subsequently fails (e.g. <c>DbUpdateConcurrencyException</c>), the entire change
    /// set rolls back together — no orphan link rows.
    /// </remarks>
    Task RemoveLinksForSagaAsync(Guid sagaId, CancellationToken cancellationToken);

    /// <summary>
    /// Stages an in-DB <c>Counter</c> row increment for the named statistic key. The row commits
    /// with the saga's next <c>SaveChangesAsync</c>; if that save fails (concurrency/unique
    /// conflict) the counter delta is rolled back along with the saga changes — counters reflect
    /// only logical-success outcomes, mirroring the OTel emit gate.
    /// </summary>
    /// <remarks>
    /// Powers the dashboard's <c>/warp/counters</c> page so saga lifecycle events show up
    /// alongside the existing <c>stats:succeeded</c> / <c>stats:failed</c> / <c>stats:deleted</c>
    /// keys. The proxy writes one base key per event (e.g. <c>stats:saga_started</c>) plus the
    /// per-hour bucket key (<c>stats:saga_started:yyyy-MM-dd-HH</c>) that drives the historical
    /// chart.
    /// </remarks>
    void RecordCounterDelta(string key, int value);
}
