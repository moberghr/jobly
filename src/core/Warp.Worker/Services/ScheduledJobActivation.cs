using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Warp.Core.Data.Queries;
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
    private readonly IWarpSqlQueries<TContext> _sqlQueries;

    public ScheduledJobActivation(
        TContext context,
        TimeProvider time,
        IWarpNotificationTransport transport,
        IOptions<WarpWorkerConfiguration> configuration,
        IWarpSqlQueries<TContext> sqlQueries)
    {
        _context = context;
        _time = time;
        _transport = transport;
        _configuration = configuration.Value;
        _sqlQueries = sqlQueries;
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

        // Single round-trip: atomically flip every due Scheduled row to Enqueued AND stream
        // back its queue. The list has one entry per activated row; deduplicate in-memory
        // so we publish one JobEnqueued notification per distinct queue.
        var activatedQueues = await _sqlQueries.ActivateScheduledJobsAsync(_context, now, ct);

        if (activatedQueues.Count == 0)
        {
            return (0, []);
        }

        var distinctQueues = new HashSet<string>(activatedQueues, StringComparer.Ordinal).ToList();
        var notifications = distinctQueues.ConvertAll(q => new Notification(NotificationKind.JobEnqueued, q));
        await NotificationDispatch.FireAsync(_transport, notifications, ct);

        return (activatedQueues.Count, distinctQueues);
    }
}
