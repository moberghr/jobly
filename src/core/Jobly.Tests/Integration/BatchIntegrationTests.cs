using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class BatchIntegrationTestsBase : IntegrationTestBase
{
    protected BatchIntegrationTestsBase(IDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GivenBatchOfFive_WhenAllComplete_ThenBatchFinalizes()
    {
        var batchPublisher = Server.CreateBatchPublisher();
        var jobs = Enumerable.Range(0, 5).Select(_ => new UnitRequest()).ToList();
        var batchId = await batchPublisher.StartNew(jobs);
        await batchPublisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        // Batch job should be completed
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId);
        batch.CurrentState.ShouldBe(State.Completed);
        batch.Kind.ShouldBe(JobKind.Batch);
        batch.JobCount.ShouldBe(5);

        // All child jobs should be completed
        var childJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job)
            .ToListAsync();
        childJobs.Count.ShouldBe(5);
        childJobs.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [Fact]
    public async Task GivenBatchWithContinuation_WhenFirstBatchCompletes_ThenContinuationActivatesAndCompletes()
    {
        var batchPublisher = Server.CreateBatchPublisher();

        var batchJobs = Enumerable.Range(0, 3).Select(_ => new UnitRequest()).ToList();
        var batchId = await batchPublisher.StartNew(batchJobs);

        var continuationJobs = Enumerable.Range(0, 2).Select(_ => new UnitRequest()).ToList();
        var continuationBatchId = await batchPublisher.ContinueBatchWith(continuationJobs, batchId);

        await batchPublisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        // First batch completed
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId);
        batch.CurrentState.ShouldBe(State.Completed);

        // Continuation batch completed
        var continuation = await ctx.Set<Job>().FirstAsync(j => j.Id == continuationBatchId);
        continuation.CurrentState.ShouldBe(State.Completed);
        continuation.ParentJobId.ShouldBe(batchId);

        // All continuation child jobs completed
        var continuationChildren = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == continuationBatchId && j.Kind == JobKind.Job)
            .ToListAsync();
        continuationChildren.Count.ShouldBe(2);
        continuationChildren.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [Fact]
    public async Task GivenThreeChainedBatches_WhenProcessed_ThenAllComplete()
    {
        var batchPublisher = Server.CreateBatchPublisher();

        // Batch 1
        var batch1Jobs = Enumerable.Range(0, 2).Select(_ => new UnitRequest()).ToList();
        var batch1Id = await batchPublisher.StartNew(batch1Jobs);

        // Batch 2 (continuation of batch 1)
        var batch2Jobs = Enumerable.Range(0, 2).Select(_ => new UnitRequest()).ToList();
        var batch2Id = await batchPublisher.ContinueBatchWith(batch2Jobs, batch1Id);

        // Batch 3 (continuation of batch 2)
        var batch3Jobs = Enumerable.Range(0, 2).Select(_ => new UnitRequest()).ToList();
        var batch3Id = await batchPublisher.ContinueBatchWith(batch3Jobs, batch2Id);

        await batchPublisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var batch1 = await ctx.Set<Job>().FirstAsync(j => j.Id == batch1Id);
        var batch2 = await ctx.Set<Job>().FirstAsync(j => j.Id == batch2Id);
        var batch3 = await ctx.Set<Job>().FirstAsync(j => j.Id == batch3Id);

        batch1.CurrentState.ShouldBe(State.Completed);
        batch2.CurrentState.ShouldBe(State.Completed);
        batch3.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenBatchWithOnAnyFinishedState_WhenSomeJobsFail_ThenContinuationStillFires()
    {
        var batchPublisher = Server.CreateBatchPublisher();

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

        await batchPublisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        // Batch with OnAnyFinishedState completes even when children fail
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId);
        batch.CurrentState.ShouldBe(State.Completed);

        // Continuation should have activated and completed because OnAnyFinishedState
        var continuation = await ctx.Set<Job>().FirstAsync(j => j.Id == continuationBatchId);
        continuation.CurrentState.ShouldBe(State.Completed);

        var continuationChildren = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == continuationBatchId && j.Kind == JobKind.Job)
            .ToListAsync();
        continuationChildren.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [Fact]
    public async Task GivenBatchWithOnlyOnSucceeded_WhenJobFails_ThenContinuationStaysAwaiting()
    {
        var batchPublisher = Server.CreateBatchPublisher();

        // Batch with failing jobs, using default OnlyOnSucceeded
        var batchJobs = new List<ThrowExceptionRequest>
        {
            new(),
            new(),
        };
        var batchId = await batchPublisher.StartNew(batchJobs, options: ContinuationOptions.OnlyOnSucceeded);

        var continuationJobs = new List<UnitRequest> { new() };
        var continuationBatchId = await batchPublisher.ContinueBatchWith(continuationJobs, batchId);

        await batchPublisher.SaveChangesAsync();

        // Wait for batch to fail
        await Server.WaitForJobState(batchId, State.Failed, timeout: TimeSpan.FromSeconds(15));

        // Give orchestration time to run, then verify continuation stays Awaiting
        await Task.Delay(2000);

        var ctx = Server.CreateContext();

        // Batch should be failed
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId);
        batch.CurrentState.ShouldBe(State.Failed);

        // Continuation stays Awaiting — condition not met, but batch could be requeued
        var continuation = await ctx.Set<Job>().FirstAsync(j => j.Id == continuationBatchId);
        continuation.CurrentState.ShouldBe(State.Awaiting);

        // Continuation children should also stay Awaiting
        var continuationChildren = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == continuationBatchId && j.Kind == JobKind.Job)
            .ToListAsync();
        continuationChildren.ShouldAllBe(j => j.CurrentState == State.Awaiting);
    }

    [Fact]
    public async Task GivenBatchWithRetryJobs_WhenRetriesExhausted_ThenBatchReflectsOutcome()
    {
        var batchPublisher = Server.CreateBatchPublisher();

        // Batch with OnlyOnSucceeded (default) and all-failing children should fail
        var failingJobs = new List<ThrowExceptionRequest> { new(), new() };
        var batchId = await batchPublisher.StartNew(failingJobs);
        await batchPublisher.SaveChangesAsync();

        await Server.WaitForJobState(batchId, State.Failed, timeout: TimeSpan.FromSeconds(15));

        var ctx = Server.CreateContext();

        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId);
        batch.CurrentState.ShouldBe(State.Failed);

        var childJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job)
            .ToListAsync();
        childJobs.ShouldAllBe(j => j.CurrentState == State.Failed);
    }
}

[Collection("PostgreSql-Integration")]
public class BatchIntegrationTests_PostgreSql : BatchIntegrationTestsBase
{
    public BatchIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture) : base(fixture) { }
}

[Collection("SqlServer-Integration")]
[Trait("Category", "SqlServer")]
public class BatchIntegrationTests_SqlServer : BatchIntegrationTestsBase
{
    public BatchIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture) : base(fixture) { }
}
