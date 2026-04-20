using Jobly.Core;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Flips jobs in <see cref="State.Scheduled"/> to <see cref="State.Enqueued"/> when their
/// <c>ScheduleTime</c> has elapsed. Time-driven, always polling — there is no event trigger
/// for "schedule time elapsed", so this task never participates in DB-push wake-up.
/// </summary>
public class ScheduledJobActivationTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    private readonly TimeSpan _interval;
    private readonly IJoblyNotificationTransport _notificationTransport;

    public ScheduledJobActivationTask(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledJobActivationTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IJoblyLockProvider lockProvider,
        TimeProvider timeProvider,
        IJoblyNotificationTransport? notificationTransport = null)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:scheduled-activation", lockProvider)
    {
        _interval = configuration.Value.ScheduledActivationInterval;
        _notificationTransport = notificationTransport ?? new NullNotificationTransport();
    }

    protected override string TaskName => "ScheduledJobActivation";

    protected override TimeSpan DefaultInterval => _interval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var result = await ActivateWithNotify(context, TimeProvider, _notificationTransport, ct);
        return result.Activated > 0 ? $"Activated {result.Activated} scheduled jobs" : null;
    }

    public static async Task<int> Activate<TCtx>(TCtx context, TimeProvider timeProvider, CancellationToken ct)
        where TCtx : DbContext
    {
        var result = await ActivateWithNotify(context, timeProvider, new NullNotificationTransport(), ct);
        return result.Activated;
    }

    internal static async Task<(int Activated, List<string> Queues)> ActivateWithNotify<TCtx>(
        TCtx context,
        TimeProvider timeProvider,
        IJoblyNotificationTransport transport,
        CancellationToken ct)
        where TCtx : DbContext
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Capture distinct queues before the flip so we can emit one JobEnqueued per queue.
        // A row could land in Scheduled between the SELECT and UPDATE — that's fine, it'll be
        // picked up on the next cycle with its own notification.
        var queues = await context.Set<Job>()
            .Where(x => x.CurrentState == State.Scheduled)
            .Where(x => x.ScheduleTime <= now)
            .Select(x => x.Queue)
            .Distinct()
            .ToListAsync(ct);

        var activated = await context.Set<Job>()
            .Where(x => x.CurrentState == State.Scheduled)
            .Where(x => x.ScheduleTime <= now)
            .ExecuteUpdateAsync(
                x => x.SetProperty(p => p.CurrentState, State.Enqueued),
                ct);

        if (activated > 0 && queues.Count > 0)
        {
            var notifications = queues.ConvertAll(q => new Notification(NotificationKind.JobEnqueued, q));
            await NotificationDispatch.FireAsync(transport, notifications, ct);
        }

        return (activated, queues);
    }
}
