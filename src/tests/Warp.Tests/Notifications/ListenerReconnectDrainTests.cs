using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Warp.Core.Enums;
using Warp.Core.Notifications;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;
using Warp.Worker;

namespace Warp.Tests.Notifications;

// Durability guarantee under a dropped listener connection: when the transport's ListenAsync
// throws (network blip, SQL failover, transport restart), the reconnect loop in
// NotificationListenerTask.ExecuteAsync fires DrainSignals on every retry iteration. That fan-out
// — dispatcher + MessageRouter + Orchestrator — lets each consumer do a fresh DB poll and pick
// up whatever was enqueued during the listener's offline window. Without this, a single
// transport hiccup would strand every job that arrived during the gap until the next slow poll.
[GenerateDatabaseTests(WithPush = true)]
public abstract class ListenerReconnectDrainTestsBase : IntegrationTestBase
{
    protected ListenerReconnectDrainTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenListenerAlwaysFails_WhenJobEnqueued_ThenReconnectDrainStillDelivers()
    {
        // Transport that always throws on ListenAsync but accepts PublishAsync silently. The
        // listener task will catch the throw, log, wait ReconnectInitialDelay, and loop back —
        // on every loop iteration DrainSignals fans a wake-up signal out to the dispatcher.
        // If drain-on-reconnect works, that's how the job gets picked up. If it doesn't, the
        // job sits until the 30s PollingInterval expires, and the test fails at its 10s cap.
        const string queue = "listener-reconnect-drain";

        var transport = new ListenFailingTransport();

        await using var server = await WarpTestServer.StartAsync(
            Fixture,
            configure: cfg =>
            {
                cfg.UseDispatcher = true;
                cfg.WorkerCount = 1;
                cfg.Queues = [queue];

                // Polling set well beyond the test budget. If polling ever delivers, the test
                // would pass at ~30s, not within the 10s TimedFact — so a pass here is proof
                // that push's drain-on-reconnect path is what delivered the job.
                cfg.PollingInterval = TimeSpan.FromSeconds(30);
                cfg.MaxPollingInterval = TimeSpan.FromSeconds(30);
                cfg.PollingIntervalFactor = 1.0;

                cfg.UseDatabasePush(o =>
                {
                    o.ChannelName = "listener_reconnect_drain_test";
                    o.ReconnectInitialDelay = TimeSpan.FromMilliseconds(500);
                    o.ReconnectMaxDelay = TimeSpan.FromSeconds(1);
                });
            },
            configureServices: services =>
            {
                services.RemoveAll<IWarpNotificationTransport>();
                services.AddSingleton<IWarpNotificationTransport>(transport);
            });

        // Deterministic: wait until the listener has STARTED its second attempt. That proves
        // the first attempt fully unwound (throw → log → backoff sleep → loop back), so the
        // startup-drain has already fired and the dispatcher is no longer running a fresh poll.
        // Any subsequent ListenAttempts increment from this point onward is a reconnect that
        // happened AFTER our enqueue below.
        await transport.SecondAttemptStarted.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        var listenAttemptsBeforeEnqueue = transport.ListenAttempts;

        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CounterRequest(), queue: queue);
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // The job must complete without polling, using only the reconnect-driven drain signal.
        // Upper bound: one reconnect backoff (~1s) + fetch + handler. 4s gives CI headroom.
        await server.WaitForJobState(jobId, State.Completed, TimeSpan.FromSeconds(4));

        transport.ListenAttempts.ShouldBeGreaterThan(
            listenAttemptsBeforeEnqueue,
            "Listener should have attempted at least one more reconnect after enqueue — that's the drain that delivered the job");
    }

    // Listener always throws, publisher is a no-op. Keeps the push side plausibly healthy
    // from the publisher's POV while guaranteeing the listener never actually observes a
    // notification — so any delivery is through the reconnect-driven DrainSignals path.
    private sealed class ListenFailingTransport : IWarpNotificationTransport
    {
        private readonly TaskCompletionSource _secondAttemptStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _listenAttempts;

        public int ListenAttempts => _listenAttempts;

        // Fires when ListenAsync is entered for the SECOND time, i.e. after the first attempt
        // fully unwound through the reconnect-backoff sleep and the listener task looped back.
        public Task SecondAttemptStarted => _secondAttemptStarted.Task;

        public Task PublishAsync(NotificationKind kind, string? queue, CancellationToken ct) => Task.CompletedTask;

        public async IAsyncEnumerable<Notification> ListenAsync([EnumeratorCancellation] CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref _listenAttempts);
            if (attempt >= 2)
            {
                _secondAttemptStarted.TrySetResult();
            }

            await Task.Yield();
            throw new InvalidOperationException("ListenFailingTransport: listen deliberately throws to exercise reconnect-drain");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}
