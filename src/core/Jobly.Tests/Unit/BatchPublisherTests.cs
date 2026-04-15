using Jobly.Core;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class BatchPublisherUnitTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected BatchPublisherUnitTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static BatchPublisher<TestContext> CreateBatchPublisher(TestContext ctx)
    {
        return new BatchPublisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task StartNew_CreatesBatchKindJobWithProcessingState()
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
        batch.CurrentState.ShouldBe(State.Processing);
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

    [Fact]
    public async Task ContinueBatchWith_InheritsParentTrace_SameContext()
    {
        // Arrange: parent and continuation batch in same context (not committed yet)
        var ctx = _fixture.CreateContext();
        var publisher = new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, new ServiceCollection().BuildServiceProvider());
        var batchPublisher = CreateBatchPublisher(ctx);

        var parentId = await publisher.Enqueue(new UnitRequest());
        var jobs = new List<UnitRequest> { new(), new() };
        var batchId = await batchPublisher.ContinueBatchWith(jobs, parentId);
        await ctx.SaveChangesAsync();

        // Assert: batch and children inherit parent's trace
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        var batch = await readCtx.Set<Job>().FindAsync(batchId);
        var children = await readCtx.Set<Job>().Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job).ToListAsync();

        parent.ShouldNotBeNull();
        batch.ShouldNotBeNull();
        batch.TraceId.ShouldBe(parent.TraceId);
        children.ShouldAllBe(c => c.TraceId == parent.TraceId);
    }

    [Fact]
    public async Task ContinueBatchWith_InheritsParentTrace_SeparateContext()
    {
        // Arrange: parent already committed
        var setupCtx = _fixture.CreateContext();
        var publisher = new Publisher<TestContext>(setupCtx, Options.Create(new JoblyConfiguration()), TimeProvider.System, new ServiceCollection().BuildServiceProvider());
        var parentId = await publisher.Enqueue(new UnitRequest());
        await setupCtx.SaveChangesAsync();

        // Act: continuation batch in new context
        var actCtx = _fixture.CreateContext();
        var batchPublisher = CreateBatchPublisher(actCtx);
        var jobs = new List<UnitRequest> { new(), new() };
        var batchId = await batchPublisher.ContinueBatchWith(jobs, parentId);
        await actCtx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        var batch = await readCtx.Set<Job>().FindAsync(batchId);
        var children = await readCtx.Set<Job>().Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job).ToListAsync();

        parent.ShouldNotBeNull();
        batch.ShouldNotBeNull();
        batch.TraceId.ShouldBe(parent.TraceId);
        children.ShouldAllBe(c => c.TraceId == parent.TraceId);
    }

    [Fact]
    public async Task StartNew_GetsOwnTrace()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var batchPublisher = CreateBatchPublisher(ctx);
        var jobs = new List<UnitRequest> { new(), new() };

        // Act
        var batchId = await batchPublisher.StartNew(jobs);
        await ctx.SaveChangesAsync();

        // Assert: batch and children share the batch's trace
        var readCtx = _fixture.CreateContext();
        var batch = await readCtx.Set<Job>().FindAsync(batchId);
        var children = await readCtx.Set<Job>().Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job).ToListAsync();

        batch.ShouldNotBeNull();
        batch.TraceId.ShouldBe(batchId);
        children.ShouldAllBe(c => c.TraceId == batchId);
    }
}

[Collection<PostgreSqlCollection>]
public class BatchPublisherUnitTests_PostgreSql : BatchPublisherUnitTestsBase
{
    public BatchPublisherUnitTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class BatchPublisherUnitTests_SqlServer : BatchPublisherUnitTestsBase
{
    public BatchPublisherUnitTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
