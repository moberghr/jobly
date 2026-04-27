using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Helper;
using Warp.Core.Retry;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.EndToEnd;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class EndToEndIntegrationTestsBase : IntegrationTestBase
{
    protected EndToEndIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    // Large integration workload (100+ jobs with retries, batches, messages, continuations) —
    // real runtime is ~15–30s under CI contention, so an explicit 90s cap keeps a legitimate
    // hang detectable without masking it behind "push + orchestration just took a while."
    [TimedFact(90_000)]
    public async Task GivenComplexWorkload_WhenProcessedByRealWorkers_ThenAllJobsReachTerminalState()
    {
        var publisher = Server.CreatePublisher();
        var batchPublisher = Server.CreateBatchPublisher();

        // 1. Simple jobs (50)
        for (var i = 0; i < 50; i++)
        {
            await publisher.Enqueue(new UnitRequest());
        }

        // 2. Jobs that spawn children (10 → 10 children = 20 total)
        for (var i = 0; i < 10; i++)
        {
            await publisher.Enqueue(new SpawnChildJobRequest());
        }

        // 3. Three-level trace chain (5 → 5 mid → 5 leaf = 15 total)
        for (var i = 0; i < 5; i++)
        {
            await publisher.Enqueue(new SpawnGrandchildJobRequest());
        }

        // 4. Jobs that spawn batches with continuations (3 parents + batches + continuations)
        for (var i = 0; i < 3; i++)
        {
            await publisher.Enqueue(new SpawnBatchRequest());
        }

        // 5. Messages with multiple handlers (5 messages → 10 jobs)
        for (var i = 0; i < 5; i++)
        {
            await publisher.Publish(new MultiRequest());
        }

        // 6. Failing jobs (10, no retries)
        for (var i = 0; i < 10; i++)
        {
            await publisher.Enqueue(new ThrowExceptionRequest());
        }

        // 7. Failing jobs with retries (5, maxRetries=2)
        for (var i = 0; i < 5; i++)
        {
            await publisher.Enqueue(new ThrowExceptionRequest(), new JobParameters().Configure<IRetryMetadata>(m => m.MaxRetries = 2));
        }

        // 8. Batch of 10 → continuation of 3
        var batchJobs = Enumerable.Range(0, 10).Select(_ => new UnitRequest()).ToList();
        var batchId = await batchPublisher.StartNew(batchJobs);
        var continuationJobs = Enumerable.Range(0, 3).Select(_ => new UnitRequest()).ToList();
        await batchPublisher.ContinueBatchWith(continuationJobs, batchId);

        // 9. Continuations: parent → child chain (5 chains of 2)
        for (var i = 0; i < 5; i++)
        {
            var parentId = await publisher.Enqueue(new UnitRequest());
            await publisher.Enqueue(new UnitRequest(), parentId);
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait for everything to complete
        await Server.WaitForCompletion(timeout: TimeSpan.FromSeconds(60));

        // Assert
        var ctx = Server.CreateContext();
        var jobs = ctx.Set<Job>().Where(j => j.Kind == JobKind.Job);

        // No stuck jobs
        var stuckJobs = await jobs
            .CountAsync(
                j => j.CurrentState == State.Enqueued
                    || j.CurrentState == State.Scheduled
                    || j.CurrentState == State.Processing
                    || j.CurrentState == State.Awaiting,
                Xunit.TestContext.Current.CancellationToken);
        stuckJobs.ShouldBe(0, "No jobs should be stuck in non-terminal states");

        // All messages completed
        var incompleteMessages = await ctx.Set<Job>()
            .CountAsync(m => m.Kind == JobKind.Message && m.CurrentState != State.Completed, Xunit.TestContext.Current.CancellationToken);
        incompleteMessages.ShouldBe(0, "All messages should be completed");

        // Job counts
        var completedJobs = await jobs.CountAsync(j => j.CurrentState == State.Completed, Xunit.TestContext.Current.CancellationToken);
        completedJobs.ShouldBeGreaterThan(100);

        var failedJobs = await jobs.CountAsync(j => j.CurrentState == State.Failed, Xunit.TestContext.Current.CancellationToken);
        failedJobs.ShouldBe(15, "10 no-retry + 5 with-retry = 15 failed");

        // All batches completed
        var incompleteBatches = await ctx.Set<Job>()
            .CountAsync(b => b.Kind == JobKind.Batch && b.CurrentState != State.Completed, Xunit.TestContext.Current.CancellationToken);
        incompleteBatches.ShouldBe(0, "All batches should be completed");

        // Trace integrity
        var jobsWithoutTrace = await jobs.CountAsync(j => j.TraceId == null, Xunit.TestContext.Current.CancellationToken);
        jobsWithoutTrace.ShouldBe(0, "All jobs should have a TraceId");

        var spawnedJobs = await jobs.CountAsync(j => j.SpawnedByJobId != null, Xunit.TestContext.Current.CancellationToken);
        spawnedJobs.ShouldBeGreaterThan(0, "Should have spawned jobs");

        // Cleanup integrity
        var jobsWithWorker = await jobs.CountAsync(j => j.CurrentWorkerId != null, Xunit.TestContext.Current.CancellationToken);
        jobsWithWorker.ShouldBe(0, "No terminal jobs should have a CurrentWorkerId");

        var jobsWithKeepAlive = await jobs.CountAsync(j => j.LastKeepAlive != null, Xunit.TestContext.Current.CancellationToken);
        jobsWithKeepAlive.ShouldBe(0, "No terminal jobs should have a LastKeepAlive");

        // Stats counts are already verified via the completed and failed Job.CurrentState
        // totals above. Asserting on the aggregated rows would require forcing the lock
        // protected server-side aggregator from the test, which is not a supported path.
        // Dedicated aggregator unit tests cover the counter-to-stat rollup in isolation.
    }
}
