using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core.Concurrency;
using Warp.Core.Data.Entities;
using Warp.Core.Enums;
using Warp.Core.Helper;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Features.Concurrency;

[GenerateDatabaseTests]
public abstract class MutexIntegrationTestsBase : IntegrationTestBase
{
    protected MutexIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenTwoJobsWithSameMutex_WhenProcessed_ThenSecondIsCancelled()
    {
        var barrier = new BarrierSignal();

        await using var server = await WarpTestServer.StartAsync(Fixture, cfg => cfg.Services.AddSingleton(barrier));
        var publisher = server.CreatePublisher();

        // Enqueue a barrier-blocked job that holds the mutex while inside its handler
        var job1Id = await publisher.Enqueue(new BarrierRequest(), new JobParameters().WithMutex("test-mutex"));
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait for the handler to *enter* — MutexPipelineBehavior calls next() only after
        // TryAcquireAsync succeeds, so this signal proves the lock is held. Removes the race
        // window that a `WaitForJobState(Processing)` check has, where State=Processing is
        // committed *before* the pipeline runs.
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // Enqueue a second job with the same mutex — must short-circuit to Deleted
        var publisher2 = server.CreatePublisher();
        var job2Id = await publisher2.Enqueue(new UnitRequest(), new JobParameters().WithMutex("test-mutex"));
        await publisher2.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(job2Id, State.Deleted);

        // Verify job2 was cancelled due to mutex
        var logs = await server.GetJobLogs(job2Id);
        logs.ShouldContain(l => l.EventType == "Deleted" && l.Message.Contains("Cancelled", StringComparison.Ordinal));

        // Job1 still in handler (held by barrier)
        var job1 = await server.GetJob(job1Id);
        job1.CurrentState.ShouldBe(State.Processing);

        // Release the barrier and let job1 complete naturally before disposal
        barrier.CanFinish.Release();
        await server.WaitForCompletion();
    }

    [TimedFact(20_000)]
    public async Task GivenTwoJobsWithSameMutex_WhenWaitMode_ThenSecondRequeuesUntilFirstFinishes()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();

        // Enqueue a slow job that holds the mutex
        var job1Id = await publisher.Enqueue(new CancellableRequest(), new JobParameters().WithMutex("test-wait", ConcurrencyMode.Wait));
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(job1Id, State.Processing);

        // Second job, same key, Wait mode — should be requeued, not deleted
        var publisher2 = server.CreatePublisher();
        var job2Id = await publisher2.Enqueue(new UnitRequest(), new JobParameters().WithMutex("test-wait", ConcurrencyMode.Wait));
        await publisher2.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Job2 should bounce off the mutex at least once and stay alive
        await server.WaitForJobLog(job2Id, "Requeued", timeout: TimeSpan.FromSeconds(5));

        var job2BeforeRelease = await server.GetJob(job2Id);
        job2BeforeRelease.CurrentState.ShouldNotBe(State.Deleted);
        job2BeforeRelease.CurrentState.ShouldNotBe(State.Completed);

        // Release job1 → job2 should now run to completion
        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(job1Id);
        await server.WaitForJobState(job1Id, State.Deleted, timeout: TimeSpan.FromSeconds(5));

        await server.WaitForJobState(job2Id, State.Completed, timeout: TimeSpan.FromSeconds(10));

        var requeuedLog = (await server.GetJobLogs(job2Id))
            .FirstOrDefault(x => string.Equals(x.EventType, "Requeued", StringComparison.Ordinal));
        requeuedLog.ShouldNotBeNull();
        requeuedLog.Message.ShouldContain("Requeued");
        requeuedLog.Message.ShouldContain("test-wait");
        requeuedLog.Message.ShouldContain("1 slots");
    }

    [TimedFact]
    public async Task GivenTwoJobsWithSameMutex_WhenWaitMode_BothCompleteAfterFirstReleases()
    {
        // Natural-completion release path for Wait-mode mutex: job1 holds the slot inside the
        // handler, job2 cannot enter while it's held, then once job1's handler returns
        // normally job2 acquires the slot and completes. Distinct from
        // ...SecondRequeuesUntilFirstFinishes, which releases via DeleteJob (cancellation).
        var barrier = new BarrierSignal();

        await using var server = await WarpTestServer.StartAsync(
            Fixture,
            cfg => cfg.Services.AddSingleton(barrier));
        var publisher = server.CreatePublisher();

        var job1Id = await publisher.Enqueue(new BarrierRequest(), new JobParameters().WithMutex("test-serialize", ConcurrencyMode.Wait));
        var job2Id = await publisher.Enqueue(new BarrierRequest(), new JobParameters().WithMutex("test-serialize", ConcurrencyMode.Wait));
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // The first job to be claimed enters the handler.
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // The other must not enter while the slot is held; 500 ms covers ~5 polling cycles.
        var spuriousEntry = await barrier.Running.WaitAsync(TimeSpan.FromMilliseconds(500), Xunit.TestContext.Current.CancellationToken);
        spuriousEntry.ShouldBeFalse("Wait-mode mutex must prevent the second job from entering while the slot is held");

        // Release the first; the second now enters.
        barrier.CanFinish.Release();
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // Release the second; both complete.
        barrier.CanFinish.Release();
        await server.WaitForCompletion();

        foreach (var id in new[] { job1Id, job2Id })
        {
            var job = await server.GetJob(id);
            job.CurrentState.ShouldBe(State.Completed);
        }
    }

    [TimedFact]
    public async Task GivenTwoJobsWithDifferentKeys_WhenWaitMode_BothEnterHandlerSimultaneously()
    {
        // Cross-key parallelism for Wait-mode mutex: distinct keys grant independent slots,
        // so two jobs with different keys must both be able to occupy the handler at the same
        // time. Per-key serialization is covered by the sibling Wait-mode tests; this test
        // only proves keys aren't globally serialized.
        var barrier = new BarrierSignal();

        await using var server = await WarpTestServer.StartAsync(
            Fixture,
            cfg => cfg.Services.AddSingleton(barrier));
        var publisher = server.CreatePublisher();

        var job1Id = await publisher.Enqueue(new BarrierRequest(), new JobParameters().WithMutex("key-A", ConcurrencyMode.Wait));
        var job2Id = await publisher.Enqueue(new BarrierRequest(), new JobParameters().WithMutex("key-B", ConcurrencyMode.Wait));
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Both must enter the handler — different keys grant independent slots. The TimedFact
        // budget bounds how long we wait for the second entry; if cross-key parallelism is
        // broken, the second await never completes and the test times out deterministically.
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        barrier.CanFinish.Release(2);
        await server.WaitForCompletion();

        foreach (var id in new[] { job1Id, job2Id })
        {
            var job = await server.GetJob(id);
            job.CurrentState.ShouldBe(State.Completed);
        }
    }
}
