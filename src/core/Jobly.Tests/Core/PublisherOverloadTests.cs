using Jobly.Core;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Core;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class PublisherOverloadTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected PublisherOverloadTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Publisher<TestContext> CreatePublisher(TestContext ctx)
    {
        return new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, new ServiceCollection().BuildServiceProvider());
    }

    [TimedFact]
    public async Task Enqueue_WithJobParameters_SetsAllFields()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);
        var parentId = Guid.NewGuid();
        var futureTime = DateTime.UtcNow.AddHours(2);

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

        var jobParams = new JobParameters
        {
            Queue = "high-priority",
            ParentId = parentId,
            ScheduleTime = futureTime,
        };

        // Act
        var id = await publisher.Enqueue(new UnitRequest(), jobParams);
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.Queue.ShouldBe("high-priority");
        job.ParentJobId.ShouldBe(parentId);
        job.ScheduleTime.ShouldBeGreaterThan(DateTime.UtcNow.AddHours(1));
    }

    [TimedFact]
    public async Task Schedule_WithQueue_SetsScheduleTimeAndQueue()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);
        var futureTime = DateTime.UtcNow.AddHours(2);

        // Act
        var id = await publisher.Schedule(new UnitRequest(), futureTime, "critical");
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.Queue.ShouldBe("critical");
        job.ScheduleTime.ShouldBeGreaterThan(DateTime.UtcNow.AddHours(1));
    }

    [TimedFact]
    public async Task Schedule_WithParent_SetsScheduleTimeAndParent()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);
        var parentId = Guid.NewGuid();
        var futureTime = DateTime.UtcNow.AddHours(2);

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
        var id = await publisher.Schedule(new UnitRequest(), futureTime, parentId);
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.ParentJobId.ShouldBe(parentId);
        job.ScheduleTime.ShouldBeGreaterThan(DateTime.UtcNow.AddHours(1));
        job.CurrentState.ShouldBe(State.Awaiting);
    }

    [TimedFact]
    public async Task Schedule_WithParentAndQueue_SetsAllFields()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);
        var parentId = Guid.NewGuid();
        var futureTime = DateTime.UtcNow.AddHours(2);

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
        var id = await publisher.Schedule(new UnitRequest(), futureTime, parentId, "critical");
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
        job.ShouldNotBeNull();
        job.ParentJobId.ShouldBe(parentId);
        job.Queue.ShouldBe("critical");
        job.ScheduleTime.ShouldBeGreaterThan(DateTime.UtcNow.AddHours(1));
    }
}
