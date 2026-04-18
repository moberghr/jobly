using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Retry;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Features.CircuitBreaker;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class CircuitBreakerIntegrationTestsBase : IntegrationTestBase
{
    protected CircuitBreakerIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenFailingHandler_WhenThresholdHit_ThenCircuitOpensAndSubsequentJobsRescheduled()
    {
        var groupKey = nameof(ThrowExceptionRequest);

        // Seed the state to threshold - 1, then drive one more failure to open the circuit.
        // This avoids waiting through the global Retry (MaxRetries=3) on every failure.
        var seedCtx = Server.CreateContext();
        seedCtx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = groupKey,
            FailureCount = 999,
            LastFailureAt = DateTime.UtcNow,
        });
        await seedCtx.SaveChangesAsync();

        var failingPublisher = Server.CreatePublisher();
        var failingJobId = await failingPublisher.Enqueue(
            new ThrowExceptionRequest(),
            new JobParameters().Configure<IRetryMetadata>(m => m.MaxRetries = 0));
        await failingPublisher.SaveChangesAsync();

        await Server.WaitForJobState(failingJobId, State.Failed, timeout: TimeSpan.FromSeconds(30));

        // Verify circuit is open
        var stateCtx = Server.CreateContext();
        var state = await stateCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == groupKey)
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBeGreaterThanOrEqualTo(1000);
        state.OpenUntil.ShouldNotBeNull();

        // Publish a subsequent job — circuit is open so it should be rescheduled
        var nextPublisher = Server.CreatePublisher();
        var nextJobId = await nextPublisher.Enqueue(new ThrowExceptionRequest());
        await nextPublisher.SaveChangesAsync();

        await Server.WaitForJobLog(nextJobId, "Requeued", timeout: TimeSpan.FromSeconds(15));

        var readCtx = Server.CreateContext();
        var nextJob = await readCtx.Set<Job>()
            .Where(x => x.Id == nextJobId)
            .FirstOrDefaultAsync(CancellationToken.None);
        nextJob.ShouldNotBeNull();
        nextJob.CurrentState.ShouldBe(State.Enqueued);
        nextJob.ScheduleTime.ShouldBeGreaterThanOrEqualTo(state.OpenUntil!.Value);

        var log = await readCtx.Set<JobLog>()
            .Where(x => x.JobId == nextJobId)
            .Where(x => x.Message.Contains("circuit breaker"))
            .FirstOrDefaultAsync(CancellationToken.None);
        log.ShouldNotBeNull();
        log.Message.ShouldContain(groupKey);

        // Cancel rescheduled job so it doesn't linger
        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(nextJobId);
    }

    [TimedFact]
    public async Task GivenSuccessAfterFailures_WhenJobSucceeds_ThenCounterResets()
    {
        var groupKey = nameof(UnitRequest);

        var seedCtx = Server.CreateContext();
        seedCtx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = groupKey,
            FailureCount = 2,
            LastFailureAt = DateTime.UtcNow,
        });
        await seedCtx.SaveChangesAsync();

        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Completed, timeout: TimeSpan.FromSeconds(30));

        var readCtx = Server.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == groupKey)
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBe(0);
        state.OpenUntil.ShouldBeNull();
    }
}
