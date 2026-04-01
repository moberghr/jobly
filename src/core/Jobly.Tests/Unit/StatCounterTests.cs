using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class StatCounterTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected StatCounterTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DeleteJob_FromCompletedState_DecrementsSucceededCounter()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        await svc.DeleteJob(jobId);

        // Assert — a Counter row for stats:succeeded with value=-1 should exist
        var readCtx = _fixture.CreateContext();
        var counterSum = await readCtx.Set<Counter>()
            .Where(c => c.Key == "stats:succeeded")
            .SumAsync(c => c.Value);
        counterSum.ShouldBe(-1);
    }

    [Fact]
    public async Task DeleteJob_FromFailedState_DecrementsFailedCounter()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        await svc.DeleteJob(jobId);

        // Assert
        var readCtx = _fixture.CreateContext();
        var counterSum = await readCtx.Set<Counter>()
            .Where(c => c.Key == "stats:failed")
            .SumAsync(c => c.Value);
        counterSum.ShouldBe(-1);
    }

    [Fact]
    public async Task DeleteJob_FromDeletedState_NoOp()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Deleted,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddDays(1),
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        await svc.DeleteJob(jobId);

        // Assert — no counter rows should be created because it was already Deleted
        var readCtx = _fixture.CreateContext();
        var counterCount = await readCtx.Set<Counter>().CountAsync();
        counterCount.ShouldBe(0);
    }

    [Fact]
    public async Task DeleteJob_AddsDeletedCounter()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        await svc.DeleteJob(jobId);

        // Assert
        var readCtx = _fixture.CreateContext();
        var deletedCounterSum = await readCtx.Set<Counter>()
            .Where(c => c.Key == "stats:deleted")
            .SumAsync(c => c.Value);
        deletedCounterSum.ShouldBe(1);
    }

    [Fact]
    public async Task RequeueJob_FromCompletedState_DecrementsSucceededCounter()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        await svc.RequeueJob(jobId);

        // Assert
        var readCtx = _fixture.CreateContext();
        var counterSum = await readCtx.Set<Counter>()
            .Where(c => c.Key == "stats:succeeded")
            .SumAsync(c => c.Value);
        counterSum.ShouldBe(-1);
    }

    [Fact]
    public async Task RequeueJob_FromFailedState_DecrementsFailedCounter()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        await svc.RequeueJob(jobId);

        // Assert
        var readCtx = _fixture.CreateContext();
        var counterSum = await readCtx.Set<Counter>()
            .Where(c => c.Key == "stats:failed")
            .SumAsync(c => c.Value);
        counterSum.ShouldBe(-1);
    }

    [Fact]
    public async Task RequeueJob_AlreadyEnqueued_NoOp()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        await svc.RequeueJob(jobId);

        // Assert — no counter rows should be created because it was already Enqueued
        var readCtx = _fixture.CreateContext();
        var counterCount = await readCtx.Set<Counter>().CountAsync();
        counterCount.ShouldBe(0);
    }
}

[Collection("PostgreSql")]
public class StatCounterTests_PostgreSql : StatCounterTestsBase
{
    public StatCounterTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class StatCounterTests_SqlServer : StatCounterTestsBase
{
    public StatCounterTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
