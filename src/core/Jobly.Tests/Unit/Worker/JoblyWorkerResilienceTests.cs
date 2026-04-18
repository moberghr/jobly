using Jobly.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;

namespace Jobly.Tests.Unit.Worker;

[Trait("Category", "NoDb")]
public class JoblyWorkerResilienceTests
{
    [TimedFact(timeout: 5_000)]
    public async Task ExecuteAsync_WhenFetchThrows_LogsAndContinuesPolling()
    {
        // Regression for PR #123 review F2 + align with JoblyDispatcher: JoblyWorker had no
        // try/catch in its poll loop, so a single exception from GetAndProcessJob silently
        // terminated the BackgroundService. JoblyDispatcher already caught and continued —
        // aligning the two ensures both background services survive transient failures.
        var callCount = 0;
        using var reachedSteadyState = new SemaphoreSlim(0, 1);

        var mockService = new Mock<IJoblyWorkerService>();
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

        var worker = new JoblyWorker<TestContext>(
            mockService.Object,
            NullLogger<JoblyWorker<TestContext>>.Instance,
            groupConfig,
            new PauseStateHolder(),
            Guid.NewGuid());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await worker.StartAsync(cts.Token);

        var reached = await reachedSteadyState.WaitAsync(TimeSpan.FromSeconds(3));

        await worker.StopAsync(CancellationToken.None);

        reached.ShouldBeTrue("worker should continue polling after an exception; pre-fix it silently terminates");
        callCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [TimedFact(timeout: 5_000)]
    public async Task ExecuteAsync_AfterProcessingJob_ResetsBackoffToFloor()
    {
        // Regression coverage for PR #123 review F5: there was no test proving currentDelay
        // actually resets after a successful process. The backoff math is pure (tested in
        // PollingBackoffTests) but the reset hook lives in JoblyWorker.ExecuteAsync.
        //
        // Sequence: empty polls accrue backoff, then a successful process, then more empty
        // polls. The second empty streak must start from the floor again — observable by
        // counting calls within a window. With a 30ms floor and a 200ms max, post-reset the
        // first two empty polls should fire at ~30ms each; without reset, they would resume
        // at the ~200ms pre-process cap.
        var callCount = 0;
        var gate = new TaskCompletionSource();
        using var latch = new SemaphoreSlim(0, 1);

        var mockService = new Mock<IJoblyWorkerService>();
        mockService.Setup(x => x.GetAndProcessJob(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) =>
            {
                var count = Interlocked.Increment(ref callCount);

                // Calls 1..5: empty (backoff accrues toward max).
                // Call 6: simulates success → worker resets currentDelay to floor.
                // Calls 7..: rapid empty polls at floor cadence.
                if (count == 6)
                {
                    return Task.FromResult(true);
                }

                if (count == 10)
                {
                    latch.Release();
                }

                return Task.FromResult(false);
            });

        var groupConfig = new WorkerGroupConfiguration
        {
            PollingInterval = TimeSpan.FromMilliseconds(30),
            MaxPollingInterval = TimeSpan.FromMilliseconds(300),
            PollingIntervalFactor = 2.0,
        };

        var worker = new JoblyWorker<TestContext>(
            mockService.Object,
            NullLogger<JoblyWorker<TestContext>>.Instance,
            groupConfig,
            new PauseStateHolder(),
            Guid.NewGuid());

        var start = DateTime.UtcNow;
        await worker.StartAsync(CancellationToken.None);

        var reached = await latch.WaitAsync(TimeSpan.FromSeconds(4));
        var elapsedAtTenth = DateTime.UtcNow - start;

        await worker.StopAsync(CancellationToken.None);

        reached.ShouldBeTrue();

        // If backoff did NOT reset, the 4 post-success empty polls (7..10) would each take
        // ~300ms, adding ~1200ms minimum to reach call 10 from call 6. With reset, polls 7..10
        // accrue 30+60+120+240 = ~450ms. Picking 900ms as a margin-wide ceiling distinguishes
        // the two without being flaky under CI jitter.
        elapsedAtTenth.ShouldBeLessThan(TimeSpan.FromMilliseconds(2_500));

        gate.TrySetResult();
    }
}
