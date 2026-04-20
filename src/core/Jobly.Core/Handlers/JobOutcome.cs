using Jobly.Core.Enums;

namespace Jobly.Core.Handlers;

public class JobOutcome
{
    public State State { get; init; }

    public DateTime? ScheduleTime { get; init; }

    public bool ClearHandlerType { get; init; }

    public string? LogMessage { get; init; }

    /// <summary>
    /// Picks the correct queued state for a reschedule: <see cref="State.Scheduled"/> when the
    /// target time is in the future (so <c>ScheduledJobActivationTask</c> owns the flip back),
    /// otherwise <see cref="State.Enqueued"/>. Every pipeline behaviour that reschedules a job
    /// (retry, circuit breaker, future mutex/rate-limit) must route through this so no new
    /// behaviour silently writes <c>Enqueued</c> + future <c>ScheduleTime</c> — which the worker
    /// fetch would pick up prematurely.
    /// </summary>
    public static State RescheduledState(DateTime scheduleTime, DateTime now) =>
        scheduleTime > now ? State.Scheduled : State.Enqueued;
}
