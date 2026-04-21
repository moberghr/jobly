using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Observability;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class TracePropagationIntegrationTestsBase : IntegrationTestBase
{
    protected TracePropagationIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenJobThatSpawnsChild_ThenChildHasSpawnedByJobId()
    {
        // Arrange
        var publisher = Server.CreatePublisher();
        var parentId = await publisher.Enqueue(new SpawnChildJobRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        await Server.WaitForCompletion();

        // Assert
        var ctx = Server.CreateContext();
        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId, Xunit.TestContext.Current.CancellationToken);
        parent.CurrentState.ShouldBe(State.Completed);

        var child = await ctx.Set<Job>()
            .FirstAsync(j => j.SpawnedByJobId == parentId, Xunit.TestContext.Current.CancellationToken);
        child.SpawnedByJobId.ShouldBe(parentId);
        child.CurrentState.ShouldBe(State.Completed);
    }

    [TimedFact]
    public async Task GivenJobThatSpawnsBatch_ThenBatchJobsHaveTraceId()
    {
        // Arrange
        var publisher = Server.CreatePublisher();
        var parentId = await publisher.Enqueue(new SpawnBatchRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        await Server.WaitForCompletion();

        // Assert
        var ctx = Server.CreateContext();
        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId, Xunit.TestContext.Current.CancellationToken);
        parent.TraceId.ShouldNotBeNull();
        var traceId = parent.TraceId!.Value;

        // The batch job (Batch kind) spawned by the handler
        var batchJob = await ctx.Set<Job>()
            .FirstAsync(j => j.Kind == JobKind.Batch && j.SpawnedByJobId == parentId, Xunit.TestContext.Current.CancellationToken);
        batchJob.TraceId.ShouldBe(traceId);

        // Child jobs of the batch should also share the trace
        var batchChildren = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == batchJob.Id && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        batchChildren.Count.ShouldBeGreaterThan(0);
        batchChildren.ShouldAllBe(j => j.TraceId == traceId);
    }

    [TimedFact]
    public async Task GivenJobThatSpawnsBatch_ThenBatchJobsHaveSpawnedByJobId()
    {
        // Arrange
        var publisher = Server.CreatePublisher();
        var parentId = await publisher.Enqueue(new SpawnBatchRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        await Server.WaitForCompletion();

        // Assert
        var ctx = Server.CreateContext();

        // The batch job spawned by the parent handler
        var batchJob = await ctx.Set<Job>()
            .FirstAsync(j => j.Kind == JobKind.Batch && j.SpawnedByJobId == parentId, Xunit.TestContext.Current.CancellationToken);
        batchJob.SpawnedByJobId.ShouldBe(parentId);
    }
}
