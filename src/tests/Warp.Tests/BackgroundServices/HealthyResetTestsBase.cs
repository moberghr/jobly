using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Warp.Core.BackgroundServices;
using Warp.Core.Data.Entities;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.BackgroundServices;

namespace Warp.Tests.BackgroundServices;

[GenerateDatabaseTests]
public abstract class HealthyResetTestsBase : IntegrationTestBase
{
    protected HealthyResetTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact(20_000)]
    public async Task ServiceRanFor5Min_ThenFaults_RestartCountResetsToZero()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var state = new HealthyResetServiceState();
        var observer = new TestStatusObserver();

        // Register the first Running waiter BEFORE starting the server so we cannot miss
        // the transition even if the supervisor sets Running synchronously during startup.
        var firstRunningReached = observer.NextStatus(nameof(HealthyResetService), BackgroundServiceStatus.Running);

        await using var server = await WarpTestServer.StartWithFakeTime(
            Fixture,
            time,
            configure: cfg => cfg.AddBackgroundService<TestContext, HealthyResetService>(),
            configureServices: services =>
            {
                services.AddSingleton(state);

                // Registered before AddBackgroundService's TryAddSingleton so this non-Try
                // call adds a second registration; .NET DI resolves the last one, giving us
                // the test observer.
                services.AddSingleton<IBackgroundServiceStatusObserver>(observer);
            });

        // Step 1: Supervisor wrote Running — first ExecuteAsync call is in-flight.
        await firstRunningReached;

        // Wait for the service itself to signal that it is inside ExecuteAsync and parked
        // on CanAdvanceTime. This ensures the startedAt timestamp is captured before we advance.
        await state.RunningGate.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // Step 2: Register Restarting waiter BEFORE releasing the first attempt so we don't
        // race the supervisor's post-fault path.
        var firstRestartingReached = observer.NextStatus(nameof(HealthyResetService), BackgroundServiceStatus.Restarting);

        // Advance fake time past the 5-minute healthy-reset threshold and release the first
        // attempt. The service returns without throwing → supervisor: RecordFault, HealthyReset
        // (ranFor ≥ 5 min → reset RestartCount to 0, reset backoffIndex), SetStatus(Restarting),
        // then Task.Delay(1s, _time, ct) which is blocked until we advance fake time.
        time.Advance(TimeSpan.FromMinutes(6));
        state.CanAdvanceTime.Release();

        await firstRestartingReached;

        // Step 3: At this point the supervisor has written Restarting and is about to park at
        // Task.Delay(1s, _time, hostStoppingToken). The observer fires BEFORE the delay is
        // created (supervisor runs a few sync statements between Restarting and Delay). Advance
        // time in small steps with Task.Yield() between each so the supervisor has a chance to
        // reach and process each advance.
        var secondRunningReached = observer.NextStatus(nameof(HealthyResetService), BackgroundServiceStatus.Running);

        while (!secondRunningReached.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        // Step 4: Second attempt is now Running. Register Restarting waiter for the second
        // fault before waiting for FaultedGate.
        var secondRestartingReached = observer.NextStatus(nameof(HealthyResetService), BackgroundServiceStatus.Restarting);

        // Wait for the service to signal that it is about to throw on the second attempt.
        await state.FaultedGate.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // Supervisor: RecordFault (RestartCount = 1, because HealthyReset cleared it to 0 after
        // the first fault), HealthyReset check (ranFor < 5 min → no reset), SetStatus(Restarting).
        await secondRestartingReached;

        // Step 5: Assert. RestartCount should be 1:
        //   - First fault: RecordFault → count=1, HealthyReset fires → count reset to 0
        //   - Second fault: RecordFault → count=1, HealthyReset does NOT fire (ranFor < 5 min)
        // Without the healthy-reset, count would be 2.
        var ctx = Fixture.CreateContext();
        var instance = await ctx.Set<BackgroundServiceInstance>()
            .Where(x => x.ServerId == server.ServerId)
            .Where(x => x.ServiceName == nameof(HealthyResetService))
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

        instance.ShouldNotBeNull();
        instance.RestartCount.ShouldBe(
            1,
            "healthy-reset cleared the counter after the 5-min first run; the second fault then adds 1");
    }
}
