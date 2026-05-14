namespace Warp.Core.Handlers;

/// <summary>
/// Marker interface for messages that auto-schedule themselves with a fixed delay when published.
/// <c>Publisher.Publish</c> detects this interface, sets <c>ScheduleTime = now + Delay</c>,
/// and parks the row in <see cref="Enums.State.Scheduled"/>. <c>ScheduledJobActivation</c> flips it
/// to <see cref="Enums.State.Enqueued"/> at the scheduled time, after which <c>MessageRouter</c>
/// routes it to its handlers.
/// </summary>
/// <remarks>
/// Primarily useful for saga timeouts:
/// <code>
/// public class OrderTimeout : ITimeoutMessage
/// {
///     [Correlate] public string OrderId { get; set; } = "";
///     public TimeSpan Delay => TimeSpan.FromMinutes(10);
/// }
/// </code>
/// When the timeout fires for a saga that already completed (and was deleted),
/// <see cref="Sagas.SagaHandlerProxy{TSaga, TMessage}"/> silently sets the outcome to
/// <c>Deleted</c> rather than failing the job — matching the documented "timeout-after-completion
/// is moot" semantics from Wolverine.
/// </remarks>
public interface ITimeoutMessage : IMessage
{
    /// <summary>
    /// Delay from <c>Publish</c>-time to delivery. Read once at publish time; subsequent reads
    /// (e.g. after deserialization) have no effect.
    /// </summary>
    TimeSpan Delay { get; }
}
