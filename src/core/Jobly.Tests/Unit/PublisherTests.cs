using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Logging;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class PublisherTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected PublisherTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Publisher<TestContext> CreatePublisher(TestContext ctx)
    {
        return new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, new ServiceCollection().BuildServiceProvider());
    }

    [TimedFact]
    public async Task Publish_CreatesMessageKindJobWithEnqueuedState()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        // Act
        var id = await publisher.Publish(new SingleHandlerMessage());
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.Kind.ShouldBe(JobKind.Message);
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task Publish_WithQueue_SetsQueueOnJob()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        // Act
        var id = await publisher.Publish(new SingleHandlerMessage(), "critical");
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.Queue.ShouldBe("critical");
    }

    [TimedFact]
    public async Task Publish_SetsTraceIdToJobId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        // Act
        var id = await publisher.Publish(new SingleHandlerMessage());
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.TraceId.ShouldBe(job.Id);
    }

    [TimedFact]
    public async Task Enqueue_CreatesJobKindJobWithEnqueuedState()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        // Act
        var id = await publisher.Enqueue(new UnitRequest());
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.Kind.ShouldBe(JobKind.Job);
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task Enqueue_WithParentId_SetsParentAndAwaitingState()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);
        var parentId = Guid.NewGuid();

        // Insert a parent job so FK is satisfied
        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act
        var id = await publisher.Enqueue(new UnitRequest(), parentId);
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.ParentJobId.ShouldBe(parentId);
        job.CurrentState.ShouldBe(State.Awaiting);
    }

    [TimedFact]
    public async Task Enqueue_CreatesJobLog()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        // Act
        var id = await publisher.Enqueue(new UnitRequest());
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var logs = await readCtx.Set<JobLog>().Where(l => l.JobId == id).ToListAsync();
        logs.Count.ShouldBe(1);
        logs[0].EventType.ShouldBe("Created");
    }

    [TimedFact]
    public async Task Schedule_SetsScheduleTime()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);
        var futureTime = DateTime.UtcNow.AddHours(2);

        // Act
        var id = await publisher.Schedule(new UnitRequest(), futureTime);
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.ScheduleTime.ShouldBeGreaterThan(DateTime.UtcNow.AddHours(1));
    }

    [TimedFact]
    public async Task Enqueue_WithParent_InheritsParentTrace_SameContext()
    {
        // Arrange: parent and child created in same context before SaveChanges
        // (matches real usage: publisher.Enqueue parent, publisher.Enqueue child, then SaveChangesAsync)
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        var parentId = await publisher.Enqueue(new UnitRequest());
        var childId = await publisher.Enqueue(new UnitRequest(), parentId);
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        var child = await readCtx.Set<Job>().FindAsync(childId);
        parent.ShouldNotBeNull();
        child.ShouldNotBeNull();
        child.TraceId.ShouldBe(parent.TraceId);
    }

    [TimedFact]
    public async Task Enqueue_WithParent_InheritsParentTrace_SeparateContext()
    {
        // Arrange: parent already committed to DB
        var setupCtx = _fixture.CreateContext();
        var setupPublisher = CreatePublisher(setupCtx);
        var parentId = await setupPublisher.Enqueue(new UnitRequest());
        await setupCtx.SaveChangesAsync();

        // Act: child created in new context
        var actCtx = _fixture.CreateContext();
        var actPublisher = CreatePublisher(actCtx);
        var childId = await actPublisher.Enqueue(new UnitRequest(), parentId);
        await actCtx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        var child = await readCtx.Set<Job>().FindAsync(childId);
        parent.ShouldNotBeNull();
        child.ShouldNotBeNull();
        child.TraceId.ShouldBe(parent.TraceId);
    }

    [TimedFact]
    public async Task Enqueue_WithoutParent_GetsOwnTrace()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        // Act
        var jobId = await publisher.Enqueue(new UnitRequest());
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.TraceId.ShouldBe(jobId);
    }

    [TimedFact]
    public async Task Publish_InsideExecutionContext_InheritsTraceAndSpawnedBy()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);
        var parentJobId = Guid.NewGuid();
        var parentTraceId = Guid.NewGuid();

        JobExecutionContext.Current = new JobExecutionInfo
        {
            JobId = parentJobId,
            TraceId = parentTraceId,
        };

        try
        {
            // Act
            var id = await publisher.Publish(new SingleHandlerMessage());
            await ctx.SaveChangesAsync();

            // Assert
            var readCtx = _fixture.CreateContext();
            var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            job.ShouldNotBeNull();
            job.TraceId.ShouldBe(parentTraceId);
            job.SpawnedByJobId.ShouldBe(parentJobId);
        }
        finally
        {
            JobExecutionContext.Current = null;
        }
    }

    [TimedFact]
    public async Task SaveChangesAsync_PersistsEnqueuedJob()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);
        var id = await publisher.Enqueue(new UnitRequest());

        // Act
        await publisher.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(id);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
    }
}

[Collection<PostgreSqlCollection>]
[Trait("Category", "PostgreSql")]
public class PublisherTests_PostgreSql : PublisherTestsBase
{
    public PublisherTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class PublisherTests_SqlServer : PublisherTestsBase
{
    public PublisherTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
