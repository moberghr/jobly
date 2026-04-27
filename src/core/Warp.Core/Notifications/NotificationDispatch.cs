using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Warp.Core.Entities;
using Warp.Core.Enums;

namespace Warp.Core.Notifications;

/// <summary>
/// Helpers for emitting push notifications from the publish-side hooks
/// (Publisher / BatchPublisher / JobCommandService / worker handler-commit path).
/// </summary>
internal static class NotificationDispatch
{
    /// <summary>
    /// Snapshots newly-added <see cref="Job"/> rows about to be committed and returns
    /// the coarse-grained notifications that should fire post-commit: one
    /// <see cref="NotificationKind.JobEnqueued"/> per distinct queue for <see cref="JobKind.Job"/>
    /// rows in <see cref="State.Enqueued"/>, and one <see cref="NotificationKind.MessageEnqueued"/>
    /// if any <see cref="JobKind.Message"/> row in <see cref="State.Enqueued"/> is added.
    /// <para>
    /// <see cref="State.Scheduled"/> rows are skipped — they fire when the activation task flips them.
    /// </para>
    /// </summary>
    public static List<Notification> CapturePending(DbContext context)
    {
        var entries = context.ChangeTracker.Entries<Job>();

        var result = new List<Notification>();
        HashSet<string>? seenQueues = null;
        var sawMessage = false;

        foreach (var entry in entries)
        {
            if (!ShouldEmit(entry))
            {
                continue;
            }

            var job = entry.Entity;
            if (job.Kind == JobKind.Job)
            {
                seenQueues ??= new HashSet<string>(StringComparer.Ordinal);
                var queue = string.IsNullOrEmpty(job.Queue) ? "default" : job.Queue;
                if (seenQueues.Add(queue))
                {
                    result.Add(new Notification(NotificationKind.JobEnqueued, queue));
                }
            }
            else if (job.Kind == JobKind.Message && !sawMessage)
            {
                result.Add(new Notification(NotificationKind.MessageEnqueued, null));
                sawMessage = true;
            }
        }

        return result;
    }

    /// <summary>
    /// Fires all captured notifications. Swallows transport failures — the originating
    /// transaction is already durable and a missed notification only delays pickup until
    /// the next listener reconnect-drain or subsequent notification.
    /// </summary>
    public static async Task FireAsync(
        IWarpNotificationTransport transport,
        IReadOnlyList<Notification> notifications,
        CancellationToken ct = default)
    {
        if (notifications.Count == 0 || transport is NullNotificationTransport)
        {
            return;
        }

        for (var i = 0; i < notifications.Count; i++)
        {
            var n = notifications[i];
            try
            {
                await transport.PublishAsync(n.Kind, n.Queue, ct);
            }
            catch
            {
                // Transport-level failure is the transport's responsibility to log.
                // Publishing must not throw upward — the commit already happened.
            }
        }
    }

    private static bool ShouldEmit(EntityEntry<Job> entry)
    {
        return entry.State == EntityState.Added
            && entry.Entity.CurrentState == State.Enqueued;
    }
}
