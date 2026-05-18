using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Warp.Worker;

namespace Warp.Tests.Worker;

[Trait("Category", "NoDb")]
public class WarpWorkerResilienceTests
{
    [TimedFact(timeout: 5_000)]
    public async Task ExecuteAsync_WhenFetchThrows_LogsAndContinuesPolling()
    {
        // Regression for PR #123 review F2 + align with WarpDispatcher: WarpWorker had no
        // try/catch in its poll loop, so a single exception from GetAndProcessJob silently
        // terminated the BackgroundService. WarpDispatcher already caught and continued —
        // aligning the two ensures both background services survive transient failures.
        var callCount = 0;
        using var reachedSteadyState = new SemaphoreSlim(0, 1);

        var mockService = new Mock<IWarpWorkerService>();
        mockService.Setup(x => x.GetAndProcessJob(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) =>
            {
                var count = Interlocked.Increment(ref callCount);
                if (count == 1)
                {
                    throw new InvalidOperationException("simulated transient DB failure");
                }

                if (count == 3)
                {
                    reachedSteadyState.Release();
                }

                return Task.FromResult(false);
            });

        var groupConfig = new WorkerGroupConfiguration
        {
            PollingInterval = TimeSpan.FromMilliseconds(50),
            MaxPollingInterval = TimeSpan.FromMilliseconds(200),
            PollingIntervalFactor = 2.0,
        };

        var worker = new WarpWorker<TestContext>(
            mockService.Object,
            NullLogger<WarpWorker<TestContext>>.Instance,
            groupConfig,
            new PauseStateHolder(),
            TimeProvider.System,
            Guid.NewGuid());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await worker.StartAsync(cts.Token);

        var reached = await reachedSteadyState.WaitAsync(TimeSpan.FromSeconds(3), Xunit.TestContext.Current.CancellationToken);

        await worker.StopAsync(CancellationToken.None);

        reached.ShouldBeTrue("worker should continue polling after an exception; pre-fix it silently terminates");
        callCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [TimedFact(timeout: 5_000)]
    public async Task ExecuteAsync_AfterProcessingJob_ResetsBackoffToFloor()
    {
        // Regression coverage for PR #123 review F5: there was no test proving currentDelay
        // actually resets after a successful process. The backoff math is pure (tested in
        // PollingBackoffTests); this test pins the reset hook in WarpWorker.ExecuteAsync.
        //
        // Earlier versions of this test asserted on wall-clock elapsed time, which flaked
        // under CI CPU starvation (a 9-cycle sleep loop accumulates scheduling jitter).
        // Instead, wrap TimeProvider so each Task.Delay call records the requested span and
        // fires the timer immediately — the worker spins through deterministically and we
        // assert on the recorded duration list.
        var floor = TimeSpan.FromMilliseconds(30);
        var max = TimeSpan.FromMilliseconds(300);
        var time = new DelayCaptureTimeProvider();

        var callCount = 0;
        var mockService = new Mock<IWarpWorkerService>();
        mockService.Setup(x => x.GetAndProcessJob(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) =>
            {
                var n = Interlocked.Increment(ref callCount);

                // Calls 1..5 empty (backoff ramps to cap). Call 6 success → reset hook fires.
                // Call 7 empty: its post-delay is the one we assert on.
                return Task.FromResult(n == 6);
            });

        var groupConfig = new WorkerGroupConfiguration
        {
            PollingInterval = floor,
            MaxPollingInterval = max,
            PollingIntervalFactor = 2.0,
        };

        var worker = new WarpWorker<TestContext>(
            mockService.Object,
            NullLogger<WarpWorker<TestContext>>.Instance,
            groupConfig,
            new PauseStateHolder(),
            time,
            Guid.NewGuid());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Expected recorded delays, in order:
            //   [0]=60   post-call-1 (empty): PollingBackoff.Next(floor)=60
            //   [1]=120  post-call-2
            //   [2]=240  post-call-3
            //   [3]=300  post-call-4 (capped at max)
            //   [4]=300  post-call-5 (still capped)
            //     -- call 6 returns true → reset to floor, `continue` (no Task.Delay) --
            //   [5]=60   post-call-7 (empty): Next(floor)=60 IFF reset happened.
            //            Without reset, currentDelay would still be max → Next(max)=300.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (time.CapturedCount < 6 && DateTime.UtcNow < deadline)
            {
                await Task.Yield();
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var delays = time.CapturedDelays;
        delays.Length.ShouldBeGreaterThanOrEqualTo(6, "worker did not progress through enough iterations");
        delays[0].ShouldBe(TimeSpan.FromMilliseconds(60));
        delays[1].ShouldBe(TimeSpan.FromMilliseconds(120));
        delays[2].ShouldBe(TimeSpan.FromMilliseconds(240));
        delays[3].ShouldBe(TimeSpan.FromMilliseconds(300));
        delays[4].ShouldBe(TimeSpan.FromMilliseconds(300));
        delays[5].ShouldBe(
            TimeSpan.FromMilliseconds(60),
            "post-success delay must be Next(floor)=60ms; observing Next(max)=300ms would mean currentDelay was not reset");
    }

    // Wraps a one-shot Task.Delay-style timer: records the requested duration, then fires
    // the callback immediately (dueTime=Zero) so the worker can spin through iterations
    // deterministically. We only need GetUtcNow + CreateTimer because Task.Delay's
    // TimeProvider overload uses exactly those two APIs.
    private sealed class DelayCaptureTimeProvider : TimeProvider
    {
        private readonly System.Threading.Lock _lock = new();
        private readonly List<TimeSpan> _delays = [];

        public int CapturedCount
        {
            get
            {
                lock (_lock)
                {
                    return _delays.Count;
                }
            }
        }

        public TimeSpan[] CapturedDelays
        {
            get
            {
                lock (_lock)
                {
                    return [.. _delays];
                }
            }
        }

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (period == Timeout.InfiniteTimeSpan)
            {
                lock (_lock)
                {
                    _delays.Add(dueTime);
                }

                return System.CreateTimer(callback, state, TimeSpan.Zero, period);
            }

            return System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
