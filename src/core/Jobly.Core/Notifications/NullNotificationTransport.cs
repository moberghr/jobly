using System.Runtime.CompilerServices;

namespace Jobly.Core.Notifications;

/// <summary>
/// Default transport when <c>AddJoblyDatabasePush()</c> is not called. Publish is a no-op;
/// listen blocks until cancelled and yields nothing. Keeps the publish-side hot path free
/// of branching — callers always just <c>await transport.PublishAsync(...)</c>.
/// </summary>
public sealed class NullNotificationTransport : IJoblyNotificationTransport
{
    public Task PublishAsync(NotificationKind kind, string? queue, CancellationToken ct) => Task.CompletedTask;

    public async IAsyncEnumerable<Notification> ListenAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // No-op listener: block forever (until cancelled). The notification listener hosted
        // service is only registered by AddJoblyDatabasePush, so this path normally isn't hit.
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        yield break;
    }
}
