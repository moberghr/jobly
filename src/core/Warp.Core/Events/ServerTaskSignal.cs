namespace Warp.Core.Events;

/// <summary>
/// Push-event channels for the in-process signal bus. Producers (workers, the notification
/// listener) call the matching <c>SignalXxx</c> method on <c>ServerTaskSignals&lt;TContext&gt;</c>;
/// consumers (server-task loops, the dashboard broadcaster) subscribe to a channel via
/// its <c>Subscribe</c> method.
/// </summary>
public enum ServerTaskSignal
{
    /// <summary>
    /// A job reached a terminal state — wake consumers that act on finalization (the
    /// orchestrator finalizes parents and activates continuations; the dashboard broadcaster
    /// fans the event out to connected clients).
    /// </summary>
    JobFinalized,

    /// <summary>
    /// A <c>Kind=Message</c> row was enqueued — wake consumers that act on message arrival
    /// (the message router fans it out into per-handler jobs; the dashboard broadcaster fans
    /// the event out to connected clients).
    /// </summary>
    MessageEnqueued,
}
