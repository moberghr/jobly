namespace Warp.Core.BackgroundServices;

/// <summary>
/// Manages <c>BackgroundServiceLease</c> rows. Provides atomic acquire / release primitives
/// for singleton service coordination. Renewal is handled in the heartbeat batch query and
/// is therefore not part of this interface.
/// </summary>
public interface IBackgroundServiceLeaseCoordinator
{
    /// <summary>
    /// Attempts to acquire the lease for <paramref name="serviceName"/>. Returns <c>true</c>
    /// when the lease is now held by this server; <c>false</c> when another server holds a
    /// live lease.
    /// <para>
    /// Acquisition semantics (transactional SELECT → INSERT/UPDATE):
    /// <list type="bullet">
    /// <item>No existing row → INSERT with <c>HolderServerId = @me</c> and
    /// <c>LeaseExpiresAt = now + ttl</c>.</item>
    /// <item>Row found with <c>LeaseExpiresAt &lt; now</c> → UPDATE to take over (expired).</item>
    /// <item>Row found with <c>HolderServerId == @me</c> → UPDATE to extend the lease (idempotent).</item>
    /// <item>Row found with <c>LeaseExpiresAt &gt;= now</c> and different holder → return <c>false</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    Task<bool> TryAcquireAsync(string serviceName, TimeSpan ttl, CancellationToken ct);

    /// <summary>
    /// Deletes the <c>BackgroundServiceLease</c> row only when the current holder matches this
    /// server. A no-op when the row is absent or held by another server.
    /// </summary>
    Task ReleaseAsync(string serviceName, CancellationToken ct);
}
