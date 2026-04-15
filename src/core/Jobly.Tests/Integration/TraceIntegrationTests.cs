using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class TraceIntegrationTestsBase : IntegrationTestBase
{
    protected TraceIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task GivenSpawnChildJobRequest_WhenProcessed_ThenChildInheritsParentTraceId()
    {
        var publisher = Server.CreatePublisher();
        var parentId = await publisher.Enqueue(new SpawnChildJobRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId);
        parent.CurrentState.ShouldBe(State.Completed);
        parent.TraceId.ShouldNotBeNull();

        // The spawned child should inherit the parent's TraceId
        var child = await ctx.Set<Job>()
            .FirstAsync(j => j.SpawnedByJobId == parentId);
        child.TraceId.ShouldBe(parent.TraceId);
        child.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenSpawnGrandchildJobRequest_WhenProcessed_ThenThreeLevelChainSharesTraceId()
    {
        var publisher = Server.CreatePublisher();
        var grandparentId = await publisher.Enqueue(new SpawnGrandchildJobRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var grandparent = await ctx.Set<Job>().FirstAsync(j => j.Id == grandparentId);
        grandparent.TraceId.ShouldNotBeNull();
        var traceId = grandparent.TraceId!.Value;

        // Middle: SpawnChildJobRequest spawned by grandparent
        var middle = await ctx.Set<Job>()
            .FirstAsync(j => j.SpawnedByJobId == grandparentId);
        middle.TraceId.ShouldBe(traceId);

        // Leaf: UnitRequest spawned by middle
        var leaf = await ctx.Set<Job>()
            .FirstAsync(j => j.SpawnedByJobId == middle.Id);
        leaf.TraceId.ShouldBe(traceId);

        // All three should be completed
        grandparent.CurrentState.ShouldBe(State.Completed);
        middle.CurrentState.ShouldBe(State.Completed);
        leaf.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenMultiHandlerMessage_WhenRouted_ThenAllHandlerJobsShareSameTraceId()
    {
        var publisher = Server.CreatePublisher();
        var messageId = await publisher.Publish(new MultiRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var message = await ctx.Set<Job>().FirstAsync(j => j.Id == messageId);
        message.TraceId.ShouldNotBeNull();

        // Both handler jobs should share the message's TraceId
        var handlerJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync();
        handlerJobs.Count.ShouldBe(2);
        handlerJobs.ShouldAllBe(j => j.TraceId == message.TraceId);
    }

    [Fact]
    public async Task GivenTwoSeparateJobs_WhenPublished_ThenEachHasDifferentTraceId()
    {
        var publisher = Server.CreatePublisher();
        var jobId1 = await publisher.Enqueue(new UnitRequest());
        var jobId2 = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var job1 = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId1);
        var job2 = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId2);

        job1.TraceId.ShouldNotBeNull();
        job2.TraceId.ShouldNotBeNull();

        // Independent jobs should have different TraceIds (each is its own trace root)
        job1.TraceId.ShouldNotBe(job2.TraceId);
    }
}

[Collection<PostgreSqlIntegrationCollection>]
public class TraceIntegrationTests_PostgreSql : TraceIntegrationTestsBase
{
    public TraceIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class TraceIntegrationTests_SqlServer : TraceIntegrationTestsBase
{
    public TraceIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
