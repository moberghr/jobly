namespace Jobly.Core.Enums;

public enum State
{
    Enqueued = 1,
    Awaiting = 2,
    Processing = 3,
    Completed = 4,
    Failed = 5,
    Deleted = 6,

    // Future-dated jobs land here and are promoted to Enqueued by ScheduledJobActivationTask
    // when ScheduleTime <= now. Separating this from Enqueued keeps the worker fetch query
    // a pure "State=Enqueued" check (no time predicate) so DB-push notifications fire only on
    // real runnable transitions.
    Scheduled = 7,
}
