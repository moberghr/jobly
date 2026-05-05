using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Orchestration;

[GenerateDatabaseTests]
public abstract class BatchIntegrationTestsBase : IntegrationTestBase
{
    protected BatchIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenBatchOfFive_WhenAllComplete_ThenBatchFinalizes()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var batchPublisher = server.CreateBatchPublisher();
        var jobs = Enumerable.Range(0, 5).Select(_ => new UnitRequest()).ToList();
        var batchId = await batchPublisher.StartNew(jobs);
        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForCompletion();

        var ctx = Fixture.CreateContext();

        // Batch job should be completed
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId, Xunit.TestContext.Current.CancellationToken);
        batch.CurrentState.ShouldBe(State.Completed);
        batch.Kind.ShouldBe(JobKind.Batch);
        batch.JobCount.ShouldBe(5);

        // All child jobs should be completed
        var childJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        childJobs.Count.ShouldBe(5);
        childJobs.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [TimedFact]
    public async Task GivenBatchWithContinuation_WhenFirstBatchCompletes_ThenContinuationActivatesAndCompletes()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var batchPublisher = server.CreateBatchPublisher();

        var batchJobs = Enumerable.Range(0, 3).Select(_ => new UnitRequest()).ToList();
        var batchId = await batchPublisher.StartNew(batchJobs);

        var continuationJobs = Enumerable.Range(0, 2).Select(_ => new UnitRequest()).ToList();
        var continuationBatchId = await batchPublisher.ContinueBatchWith(continuationJobs, batchId);

        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForCompletion();

        var ctx = Fixture.CreateContext();

        // First batch completed
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId, Xunit.TestContext.Current.CancellationToken);
        batch.CurrentState.ShouldBe(State.Completed);

        // Continuation batch completed
        var continuation = await ctx.Set<Job>().FirstAsync(j => j.Id == continuationBatchId, Xunit.TestContext.Current.CancellationToken);
        continuation.CurrentState.ShouldBe(State.Completed);
        continuation.ParentJobId.ShouldBe(batchId);

        // All continuation child jobs completed
        var continuationChildren = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == continuationBatchId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        continuationChildren.Count.ShouldBe(2);
        continuationChildren.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [TimedFact]
    public async Task GivenThreeChainedBatches_WhenProcessed_ThenAllComplete()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var batchPublisher = server.CreateBatchPublisher();

        // Batch 1
        var batch1Jobs = Enumerable.Range(0, 2).Select(_ => new UnitRequest()).ToList();
        var batch1Id = await batchPublisher.StartNew(batch1Jobs);

        // Batch 2 (continuation of batch 1)
        var batch2Jobs = Enumerable.Range(0, 2).Select(_ => new UnitRequest()).ToList();
        var batch2Id = await batchPublisher.ContinueBatchWith(batch2Jobs, batch1Id);

        // Batch 3 (continuation of batch 2)
        var batch3Jobs = Enumerable.Range(0, 2).Select(_ => new UnitRequest()).ToList();
        var batch3Id = await batchPublisher.ContinueBatchWith(batch3Jobs, batch2Id);

        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForCompletion();

        var ctx = Fixture.CreateContext();

        var batch1 = await ctx.Set<Job>().FirstAsync(j => j.Id == batch1Id, Xunit.TestContext.Current.CancellationToken);
        var batch2 = await ctx.Set<Job>().FirstAsync(j => j.Id == batch2Id, Xunit.TestContext.Current.CancellationToken);
        var batch3 = await ctx.Set<Job>().FirstAsync(j => j.Id == batch3Id, Xunit.TestContext.Current.CancellationToken);

        batch1.CurrentState.ShouldBe(State.Completed);
        batch2.CurrentState.ShouldBe(State.Completed);
        batch3.CurrentState.ShouldBe(State.Completed);
    }

    [TimedFact]
    public async Task GivenBatchWithOnAnyFinishedState_WhenSomeJobsFail_ThenContinuationStillFires()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var batchPublisher = server.CreateBatchPublisher();

        // Batch with mix of succeeding and failing jobs, using OnAnyFinishedState
        var batchJobs = new List<ThrowExceptionRequest>
        {
            new(),
            new(),
        };
        var batchId = await batchPublisher.StartNew(batchJobs, options: ContinuationOptions.OnAnyFinishedState);

        // Continuation should fire even though batch jobs fail
        var continuationJobs = new List<UnitRequest> { new() };
        var continuationBatchId = await batchPublisher.ContinueBatchWith(continuationJobs, batchId);

        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForCompletion();

        var ctx = Fixture.CreateContext();

        // Batch with OnAnyFinishedState completes even when children fail
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId, Xunit.TestContext.Current.CancellationToken);
        batch.CurrentState.ShouldBe(State.Completed);

        // Continuation should have activated and completed because OnAnyFinishedState
        var continuation = await ctx.Set<Job>().FirstAsync(j => j.Id == continuationBatchId, Xunit.TestContext.Current.CancellationToken);
        continuation.CurrentState.ShouldBe(State.Completed);

        var continuationChildren = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == continuationBatchId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        continuationChildren.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [TimedFact]
    public async Task GivenBatchWithOnlyOnSucceeded_WhenJobFails_ThenContinuationStaysAwaiting()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var batchPublisher = server.CreateBatchPublisher();

        // Batch with failing jobs, using default OnlyOnSucceeded
        var batchJobs = new List<ThrowExceptionRequest>
        {
            new(),
            new(),
        };
        var batchId = await batchPublisher.StartNew(batchJobs, options: ContinuationOptions.OnlyOnSucceeded);

        var continuationJobs = new List<UnitRequest> { new() };
        var continuationBatchId = await batchPublisher.ContinueBatchWith(continuationJobs, batchId);

        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait for batch to fail
        await server.WaitForJobState(batchId, State.Failed, timeout: TimeSpan.FromSeconds(8));

        // Give orchestration a few ticks (100ms interval in the test server) to confirm the
        // continuation is not activated. 500ms covers ~5 passes — more than enough to catch
        // an erroneous activation without adding 2s to the test.
        await Task.Delay(500, Xunit.TestContext.Current.CancellationToken);

        var ctx = Fixture.CreateContext();

        // Batch should be failed
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId, Xunit.TestContext.Current.CancellationToken);
        batch.CurrentState.ShouldBe(State.Failed);

        // Continuation stays Awaiting — condition not met, but batch could be requeued
        var continuation = await ctx.Set<Job>().FirstAsync(j => j.Id == continuationBatchId, Xunit.TestContext.Current.CancellationToken);
        continuation.CurrentState.ShouldBe(State.Awaiting);

        // Continuation children should also stay Awaiting
        var continuationChildren = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == continuationBatchId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        continuationChildren.ShouldAllBe(j => j.CurrentState == State.Awaiting);
    }

    [TimedFact]
    public async Task GivenBatchWithRetryJobs_WhenRetriesExhausted_ThenBatchReflectsOutcome()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var batchPublisher = server.CreateBatchPublisher();

        // Batch with OnlyOnSucceeded (default) and all-failing children should fail
        var failingJobs = new List<ThrowExceptionRequest> { new(), new() };
        var batchId = await batchPublisher.StartNew(failingJobs);
        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(batchId, State.Failed, timeout: TimeSpan.FromSeconds(8));

        var ctx = Fixture.CreateContext();

        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId, Xunit.TestContext.Current.CancellationToken);
        batch.CurrentState.ShouldBe(State.Failed);

        var childJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        childJobs.ShouldAllBe(j => j.CurrentState == State.Failed);
    }
}
