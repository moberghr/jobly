using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class PublisherTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected PublisherTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Publisher<TestContext> CreatePublisher(TestContext ctx)
    {
        return new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()));
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
}

[Collection("PostgreSql")]
public class PublisherTests_PostgreSql : PublisherTestsBase
{
    public PublisherTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class PublisherTests_SqlServer : PublisherTestsBase
{
    public PublisherTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
