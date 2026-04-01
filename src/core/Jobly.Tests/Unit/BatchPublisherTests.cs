using Jobly.Core;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

[Collection("PostgreSql")]
public class BatchPublisherUnitTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public BatchPublisherUnitTests(PostgreSqlFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static BatchPublisher<TestContext> CreateBatchPublisher(TestContext ctx)
    {
        return new BatchPublisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()));
    }

    [Fact]
    public async Task StartNew_CreatesBatchKindJobWithAwaitingState()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreateBatchPublisher(ctx);
        var jobs = new List<UnitRequest> { new(), new(), new() };

        // Act
        var batchId = await publisher.StartNew(jobs);
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var batch = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == batchId);
        batch.ShouldNotBeNull();
        batch.Kind.ShouldBe(JobKind.Batch);
        batch.CurrentState.ShouldBe(State.Awaiting);
    }

    [Fact]
    public async Task StartNew_CreatesChildJobsWithEnqueuedState()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreateBatchPublisher(ctx);
        var jobs = new List<UnitRequest> { new(), new(), new() };

        // Act
        var batchId = await publisher.StartNew(jobs);
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job)
            .ToListAsync();
        children.Count.ShouldBe(3);
        children.ShouldAllBe(c => c.CurrentState == State.Enqueued);
    }

    [Fact]
    public async Task StartNew_SetsJobCountOnBatch()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreateBatchPublisher(ctx);
        var jobs = new List<UnitRequest> { new(), new(), new() };

        // Act
        var batchId = await publisher.StartNew(jobs);
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var batch = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == batchId);
        batch.ShouldNotBeNull();
        batch.JobCount.ShouldBe(3);
    }

    [Fact]
    public async Task ContinueBatchWith_CreatesBatchWithParentId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreateBatchPublisher(ctx);

        // Create first batch
        var firstBatchId = await publisher.StartNew(new List<UnitRequest> { new() });
        await ctx.SaveChangesAsync();

        // Act
        var ctx2 = _fixture.CreateContext();
        var publisher2 = CreateBatchPublisher(ctx2);
        var continuationId = await publisher2.ContinueBatchWith(new List<UnitRequest> { new(), new() }, firstBatchId);
        await ctx2.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var continuation = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == continuationId);
        continuation.ShouldNotBeNull();
        continuation.ParentJobId.ShouldBe(firstBatchId);
        continuation.Kind.ShouldBe(JobKind.Batch);
    }

    [Fact]
    public async Task ContinueBatchWith_ChildrenAreAwaiting()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreateBatchPublisher(ctx);

        var firstBatchId = await publisher.StartNew(new List<UnitRequest> { new() });
        await ctx.SaveChangesAsync();

        // Act
        var ctx2 = _fixture.CreateContext();
        var publisher2 = CreateBatchPublisher(ctx2);
        var continuationId = await publisher2.ContinueBatchWith(new List<UnitRequest> { new(), new() }, firstBatchId);
        await ctx2.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == continuationId && j.Kind == JobKind.Job)
            .ToListAsync();
        children.Count.ShouldBe(2);
        children.ShouldAllBe(c => c.CurrentState == State.Awaiting);
    }

    [Fact]
    public async Task StartNew_WithName_SetsTypeField()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreateBatchPublisher(ctx);
        var jobs = new List<UnitRequest> { new(), new() };

        // Act
        var batchId = await publisher.StartNew(jobs, name: "MyBatchName");
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var batch = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == batchId);
        batch.ShouldNotBeNull();
        batch.Type.ShouldBe("MyBatchName");
    }
}
