using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Observability;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class TraceIntegrationTestsBase : IntegrationTestBase
{
    protected TraceIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenSpawnChildJobRequest_WhenProcessed_ThenChildInheritsParentTraceId()
    {
        var publisher = Server.CreatePublisher();
        var parentId = await publisher.Enqueue(new SpawnChildJobRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId, Xunit.TestContext.Current.CancellationToken);
        parent.CurrentState.ShouldBe(State.Completed);
        parent.TraceId.ShouldNotBeNull();

        // The spawned child should inherit the parent's TraceId
        var child = await ctx.Set<Job>()
            .FirstAsync(j => j.SpawnedByJobId == parentId, Xunit.TestContext.Current.CancellationToken);
        child.TraceId.ShouldBe(parent.TraceId);
        child.CurrentState.ShouldBe(State.Completed);
    }

    [TimedFact]
    public async Task GivenSpawnGrandchildJobRequest_WhenProcessed_ThenThreeLevelChainSharesTraceId()
    {
        var publisher = Server.CreatePublisher();
        var grandparentId = await publisher.Enqueue(new SpawnGrandchildJobRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var grandparent = await ctx.Set<Job>().FirstAsync(j => j.Id == grandparentId, Xunit.TestContext.Current.CancellationToken);
        grandparent.TraceId.ShouldNotBeNull();
        var traceId = grandparent.TraceId!.Value;

        // Middle: SpawnChildJobRequest spawned by grandparent
        var middle = await ctx.Set<Job>()
            .FirstAsync(j => j.SpawnedByJobId == grandparentId, Xunit.TestContext.Current.CancellationToken);
        middle.TraceId.ShouldBe(traceId);

        // Leaf: UnitRequest spawned by middle
        var leaf = await ctx.Set<Job>()
            .FirstAsync(j => j.SpawnedByJobId == middle.Id, Xunit.TestContext.Current.CancellationToken);
        leaf.TraceId.ShouldBe(traceId);

        // All three should be completed
        grandparent.CurrentState.ShouldBe(State.Completed);
        middle.CurrentState.ShouldBe(State.Completed);
        leaf.CurrentState.ShouldBe(State.Completed);
    }

    [TimedFact]
    public async Task GivenMultiHandlerMessage_WhenRouted_ThenAllHandlerJobsShareSameTraceId()
    {
        var publisher = Server.CreatePublisher();
        var messageId = await publisher.Publish(new MultiRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var message = await ctx.Set<Job>().FirstAsync(j => j.Id == messageId, Xunit.TestContext.Current.CancellationToken);
        message.TraceId.ShouldNotBeNull();

        // Both handler jobs should share the message's TraceId
        var handlerJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        handlerJobs.Count.ShouldBe(2);
        handlerJobs.ShouldAllBe(j => j.TraceId == message.TraceId);
    }

    [TimedFact]
    public async Task GivenTwoSeparateJobs_WhenPublished_ThenEachHasDifferentTraceId()
    {
        var publisher = Server.CreatePublisher();
        var jobId1 = await publisher.Enqueue(new UnitRequest());
        var jobId2 = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var job1 = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId1, Xunit.TestContext.Current.CancellationToken);
        var job2 = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId2, Xunit.TestContext.Current.CancellationToken);

        job1.TraceId.ShouldNotBeNull();
        job2.TraceId.ShouldNotBeNull();

        // Independent jobs should have different TraceIds (each is its own trace root)
        job1.TraceId.ShouldNotBe(job2.TraceId);
    }
}
