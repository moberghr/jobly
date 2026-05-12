using Microsoft.EntityFrameworkCore;

namespace Warp.Core.Events;

/// <summary>
/// In-process push → wake plumbing. Producers (workers finalising a job, the notification
/// listener receiving a remote push, scheduled-job activation) call the named
/// <c>SignalXxx</c> methods. Consumers — server-task loops in <c>Warp.Worker</c> and the
/// dashboard broadcaster in <c>Warp.UI</c> — subscribe at host construction and tear down
/// on shutdown via the returned <see cref="IDisposable"/>.
/// </summary>
/// <remarks>
/// Lives in <c>Warp.Core</c> so that any downstream package (Worker, UI, future addons) can
/// subscribe without taking a dependency on Worker. A dedicated method per signal (instead
/// of a keyed registry) matches how Warp's signal surface is actually used: a small,
/// domain-fixed set that changes rarely. Adding a signal is a one-line addition here plus
/// whatever subscriber code needs it — no keys, no stringly typed channels.
/// </remarks>
public sealed class ServerTaskSignals<TContext>
    where TContext : DbContext
{
    private readonly List<Action> _jobFinalizedWakers = [];
    private readonly List<Action> _messageEnqueuedWakers = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Called when a job reaches a terminal state — wakes all subscribers to
    /// <see cref="ServerTaskSignal.JobFinalized"/>.
    /// </summary>
    public void SignalJobFinalized() => Fire(_jobFinalizedWakers);

    /// <summary>
    /// Called when a <c>Kind=Message</c> job is enqueued — wakes all subscribers to
    /// <see cref="ServerTaskSignal.MessageEnqueued"/>.
    /// </summary>
    public void SignalMessageEnqueued() => Fire(_messageEnqueuedWakers);

    /// <summary>
    /// Subscribe <paramref name="wake"/> to the given channel. Dispose the returned handle to
    /// unregister — leaking it would leak a closure across the host's lifetime. Disposal is
    /// idempotent and thread-safe.
    /// </summary>
    public IDisposable Subscribe(ServerTaskSignal channel, Action wake) => channel switch
    {
        ServerTaskSignal.JobFinalized => Subscribe(_jobFinalizedWakers, wake),
        ServerTaskSignal.MessageEnqueued => Subscribe(_messageEnqueuedWakers, wake),
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown server-task signal channel."),
    };

    private Subscription Subscribe(List<Action> list, Action wake)
    {
        lock (_gate)
        {
            list.Add(wake);
        }

        return new Subscription(list, wake, _gate);
    }

    private void Fire(List<Action> list)
    {
        Action[] snapshot;
        lock (_gate)
        {
            snapshot = [.. list];
        }

        foreach (var waker in snapshot)
        {
            waker();
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly List<Action> _list;
        private readonly Lock _gate;
        private Action? _wake;

        public Subscription(List<Action> list, Action wake, Lock gate)
        {
            _list = list;
            _gate = gate;
            _wake = wake;
        }

        public void Dispose()
        {
            if (_wake is null)
            {
                return;
            }

            lock (_gate)
            {
                _list.Remove(_wake);
            }

            _wake = null;
        }
    }
}
