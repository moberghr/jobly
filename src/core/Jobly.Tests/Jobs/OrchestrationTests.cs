using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    // === Finalization ===

    [Fact]
    public async Task GivenBatchWithAllChildrenCompleted_WhenOrchestrationRuns_ThenBatchIsFinalized()
    {
        var context = CreateContext();
        var batchId = await CreateBatch(context, 3);
        await context.SaveChangesAsync();

        // Process all batch jobs
        await ProcessAllJobs();

        var batch = await GetJob(batchId);
        batch.CurrentState.ShouldBe(State.Completed);
        batch.ExpireAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenBatchWithSomeChildrenStillRunning_WhenOrchestrationRuns_ThenBatchStaysAwaiting()
    {
        var context = CreateContext();
        var batchId = await CreateBatch(context, 3);
        await context.SaveChangesAsync();

        // Process only one job
        await ProcessJob();

        // Run orchestration — batch should NOT be finalized yet
        await RunOrchestration();

        var batch = await GetJob(batchId);
        batch.CurrentState.ShouldBe(State.Awaiting);
    }

    [Fact]
    public async Task GivenBatchWithFailedChild_WhenOnlyOnSucceeded_ThenBatchIsFailed()
    {
        var context = CreateContext();
        var batchId = await CreateBatchWithOptions(context, 2, ContinuationOptions.OnlyOnSucceeded);
        await context.SaveChangesAsync();

        // Manually fail one child
        var children = await CreateContext().Set<Job>()
            .Where(x => x.ParentJobId == batchId && x.Kind == JobKind.Job)
            .ToListAsync();

        var failContext = CreateContext();
        var child1 = await failContext.Set<Job>().FindAsync(children[0].Id);
        child1!.CurrentState = State.Failed;
        var child2 = await failContext.Set<Job>().FindAsync(children[1].Id);
        child2!.CurrentState = State.Completed;
        await failContext.SaveChangesAsync();

        await RunOrchestration();

        var batch = await GetJob(batchId);
        batch.CurrentState.ShouldBe(State.Failed);
    }

    [Fact]
    public async Task GivenBatchWithFailedChild_WhenOnAnyFinishedState_ThenBatchIsCompleted()
    {
        var context = CreateContext();
        var batchId = await CreateBatchWithOptions(context, 2, ContinuationOptions.OnAnyFinishedState);
        await context.SaveChangesAsync();

        // Manually set children to terminal states
        var children = await CreateContext().Set<Job>()
            .Where(x => x.ParentJobId == batchId && x.Kind == JobKind.Job)
            .ToListAsync();

        var failContext = CreateContext();
        var child1 = await failContext.Set<Job>().FindAsync(children[0].Id);
        child1!.CurrentState = State.Failed;
        var child2 = await failContext.Set<Job>().FindAsync(children[1].Id);
        child2!.CurrentState = State.Completed;
        await failContext.SaveChangesAsync();

        await RunOrchestration();

        var batch = await GetJob(batchId);
        batch.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenMessageWithAllHandlerJobsCompleted_WhenOrchestrationRuns_ThenMessageIsFinalized()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        var message = await GetMessage(messageId);
        message.CurrentState.ShouldBe(State.Completed);
        message.ExpireAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenOrchestrationRunTwice_WhenAlreadyFinalized_ThenNoDoubleFinalization()
    {
        var context = CreateContext();
        var batchId = await CreateBatch(context, 2);
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        var batch1 = await GetJob(batchId);
        batch1.CurrentState.ShouldBe(State.Completed);
        var expireAt1 = batch1.ExpireAt;

        // Run orchestration again — should be idempotent
        await RunOrchestration();

        var batch2 = await GetJob(batchId);
        batch2.CurrentState.ShouldBe(State.Completed);
        batch2.ExpireAt.ShouldBe(expireAt1);
    }

    // === Continuation activation ===

    [Fact]
    public async Task GivenBatchWithContinuation_WhenFirstBatchCompletes_ThenContinuationChildrenAreEnqueued()
    {
        var context = CreateContext();
        var batch1Id = await CreateBatch(context, 2);
        var batch2Id = await ContinueBatchWith(context, 3, batch1Id);
        await context.SaveChangesAsync();

        // Process first batch only
        await ProcessJob();
        await ProcessJob();
        await RunOrchestration();

        // Continuation batch's children should now be Enqueued
        var batch2Children = await CreateContext().Set<Job>()
            .Where(x => x.ParentJobId == batch2Id && x.Kind == JobKind.Job)
            .ToListAsync();

        batch2Children.Count.ShouldBe(3);
        batch2Children.ShouldAllBe(j => j.CurrentState == State.Enqueued);
    }

    [Fact]
    public async Task GivenChainedContinuations_WhenAllProcessed_ThenAllBatchesComplete()
    {
        var context = CreateContext();
        var batch1Id = await CreateBatch(context, 2);
        var batch2Id = await ContinueBatchWith(context, 2, batch1Id);
        var batch3Id = await ContinueBatchWith(context, 2, batch2Id);
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        var batch1 = await GetJob(batch1Id);
        var batch2 = await GetJob(batch2Id);
        var batch3 = await GetJob(batch3Id);

        batch1.CurrentState.ShouldBe(State.Completed);
        batch2.CurrentState.ShouldBe(State.Completed);
        batch3.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenRegularJobWithAwaitingChild_WhenParentCompletes_ThenChildIsActivated()
    {
        var context = CreateContext();
        var parentId = await TestUtils.CreatePublisher(context).Enqueue(new UnitRequest());
        var childId = await CreateJobWithParentId(context, parentId);
        await context.SaveChangesAsync();

        // Process parent only
        await ProcessJob();
        await RunOrchestration();

        var child = await GetJob(childId);
        child.CurrentState.ShouldBe(State.Enqueued);
    }

    // === Worker isolation ===

    [Fact]
    public async Task GivenBatchChild_WhenWorkerCompletesIt_ThenBatchStaysAwaitingUntilOrchestration()
    {
        var context = CreateContext();
        var batchId = await CreateBatch(context, 1);
        await context.SaveChangesAsync();

        // Worker processes the only child
        await ProcessJob();

        // Before orchestration: batch should still be Awaiting
        var batchBefore = await GetJob(batchId);
        batchBefore.CurrentState.ShouldBe(State.Awaiting);

        // After orchestration: batch should be Completed
        await RunOrchestration();

        var batchAfter = await GetJob(batchId);
        batchAfter.CurrentState.ShouldBe(State.Completed);
    }
}
