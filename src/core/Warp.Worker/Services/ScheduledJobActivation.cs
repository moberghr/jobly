using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Notifications;

namespace Warp.Worker.Services;

/// <summary>
/// Flips jobs in <see cref="State.Scheduled"/> to <see cref="State.Enqueued"/> when their
/// <c>ScheduleTime</c> has elapsed. Time-driven, always polling — there is no event trigger
/// for "schedule time elapsed", so this task never participates in DB-push wake-up.
/// </summary>
public sealed class ScheduledJobActivation<TContext> : IServerTask
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _time;
    private readonly IWarpNotificationTransport _transport;
    private readonly WarpWorkerConfiguration _configuration;

    public ScheduledJobActivation(
        TContext context,
        TimeProvider time,
        IWarpNotificationTransport transport,
        IOptions<WarpWorkerConfiguration> configuration)
    {
        _context = context;
        _time = time;
        _transport = transport;
        _configuration = configuration.Value;
    }

    public string Name => "ScheduledJobActivation";

    public string? LockKey => "warp:scheduled-activation";

    public TimeSpan? DefaultInterval => _configuration.ScheduledActivationInterval;

    public async Task<string?> ExecuteAsync(CancellationToken ct)
    {
        var result = await ActivateWithNotifyAsync(ct);

        return result.Activated > 0 ? $"Activated {result.Activated} scheduled jobs" : null;
    }

    internal async Task<(int Activated, List<string> Queues)> ActivateWithNotifyAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var queues = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Scheduled)
            .Where(x => x.ScheduleTime <= now)
            .Select(x => x.Queue)
            .Distinct()
            .ToListAsync(ct);

        var activated = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Scheduled)
            .Where(x => x.ScheduleTime <= now)
            .ExecuteUpdateAsync(
                x => x.SetProperty(p => p.CurrentState, State.Enqueued),
                ct);

        if (activated > 0 && queues.Count > 0)
        {
            var notifications = queues.ConvertAll(q => new Notification(NotificationKind.JobEnqueued, q));
            await NotificationDispatch.FireAsync(_transport, notifications, ct);
        }

        return (activated, queues);
    }
}
