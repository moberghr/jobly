namespace Jobly.Core.Notifications;

/// <summary>
/// Provider-agnostic notification transport. The publish side is called post-commit from
/// Publisher/BatchPublisher/JobCommandService/worker completion paths. The listen side is
/// consumed by a single long-lived hosted service (<c>NotificationListenerTask</c>) that
/// signals the appropriate background tasks on each event.
/// <para>
/// The default registration is <see cref="NullNotificationTransport"/> (no-op); users opt
/// into DB-push by calling <c>opt.UseDatabasePush() (inside the AddJobly/AddJoblyWorker lambda)</c>.
/// </para>
/// </summary>
public interface IJoblyNotificationTransport
{
    /// <summary>
    /// Emits a notification. Must be called AFTER the originating transaction commits —
    /// otherwise consumers may wake before the committed state is visible. Failures are
    /// the transport's responsibility to log/swallow; publish must not throw, because the
    /// originating transaction is already durable.
    /// </summary>
    Task PublishAsync(NotificationKind kind, string? queue, CancellationToken ct);

    /// <summary>
    /// Yields notifications as they arrive. The transport owns the long-lived connection
    /// and reconnect loop internally; the returned sequence should survive transient
    /// failures and only terminate when <paramref name="ct"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<Notification> ListenAsync(CancellationToken ct);
}
