using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Orchestration;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class ContinuationIntegrationTestsBase : IntegrationTestBase
{
    protected ContinuationIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenParentJob_WhenCompletes_ThenChildActivatesAndCompletes()
    {
        var publisher = Server.CreatePublisher();
        var parentId = await publisher.Enqueue(new UnitRequest());
        var childId = await publisher.Enqueue(new UnitRequest(), parentId);
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId, Xunit.TestContext.Current.CancellationToken);
        parent.CurrentState.ShouldBe(State.Completed);

        var child = await ctx.Set<Job>().FirstAsync(j => j.Id == childId, Xunit.TestContext.Current.CancellationToken);
        child.CurrentState.ShouldBe(State.Completed);
        child.ParentJobId.ShouldBe(parentId);
    }

    [TimedFact]
    public async Task GivenParentJobThatFails_WhenDefaultOnlyOnSucceeded_ThenChildStaysAwaiting()
    {
        var publisher = Server.CreatePublisher();
        var parentId = await publisher.Enqueue(new ThrowExceptionRequest());
        var childId = await publisher.Enqueue(new UnitRequest(), parentId);
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait for parent to fail
        await Server.WaitForJobState(parentId, State.Failed, timeout: TimeSpan.FromSeconds(8));

        // Give orchestration a few ticks (100ms interval in the test server) to confirm the
        // child is not activated. 500ms covers ~5 passes without stalling the test for 2s.
        await Task.Delay(500, Xunit.TestContext.Current.CancellationToken);

        var ctx = Server.CreateContext();

        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId, Xunit.TestContext.Current.CancellationToken);
        parent.CurrentState.ShouldBe(State.Failed);

        // Child stays Awaiting — condition not met, but parent could be requeued
        var child = await ctx.Set<Job>().FirstAsync(j => j.Id == childId, Xunit.TestContext.Current.CancellationToken);
        child.CurrentState.ShouldBe(State.Awaiting);
    }

    [TimedFact]
    public async Task GivenParentJobThatFails_WithOnAnyFinishedState_ThenChildStillActivates()
    {
        // Use batch publisher to create a batch with OnAnyFinishedState continuation,
        // since individual job continuations don't have ContinuationOptions on the child.
        // Batch is the mechanism for continuation options.
        var batchPublisher = Server.CreateBatchPublisher();

        var batchJobs = new List<ThrowExceptionRequest> { new() };
        var batchId = await batchPublisher.StartNew(batchJobs, options: ContinuationOptions.OnAnyFinishedState);

        var continuationJobs = new List<UnitRequest> { new() };
        var continuationBatchId = await batchPublisher.ContinueBatchWith(continuationJobs, batchId);

        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        // Batch with OnAnyFinishedState completes even when children fail
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId, Xunit.TestContext.Current.CancellationToken);
        batch.CurrentState.ShouldBe(State.Completed);

        // Continuation batch activated and completed because OnAnyFinishedState
        var continuation = await ctx.Set<Job>().FirstAsync(j => j.Id == continuationBatchId, Xunit.TestContext.Current.CancellationToken);
        continuation.CurrentState.ShouldBe(State.Completed);

        var continuationChildren = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == continuationBatchId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        continuationChildren.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [TimedFact]
    public async Task GivenThreeLevelContinuationChain_WhenProcessed_ThenAllComplete()
    {
        var publisher = Server.CreatePublisher();
        var grandparentId = await publisher.Enqueue(new UnitRequest());
        var parentId = await publisher.Enqueue(new UnitRequest(), grandparentId);
        var childId = await publisher.Enqueue(new UnitRequest(), parentId);
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var grandparent = await ctx.Set<Job>().FirstAsync(j => j.Id == grandparentId, Xunit.TestContext.Current.CancellationToken);
        grandparent.CurrentState.ShouldBe(State.Completed);

        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId, Xunit.TestContext.Current.CancellationToken);
        parent.CurrentState.ShouldBe(State.Completed);

        var child = await ctx.Set<Job>().FirstAsync(j => j.Id == childId, Xunit.TestContext.Current.CancellationToken);
        child.CurrentState.ShouldBe(State.Completed);
    }
}
