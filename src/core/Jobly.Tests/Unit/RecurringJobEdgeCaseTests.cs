using System.Text.Json;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class RecurringJobEdgeCaseTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected RecurringJobEdgeCaseTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ScheduleRecurringJobs_JobStillPending_SkipsScheduling()
    {
        // Arrange — recurring job with NextJobId pointing to an Enqueued (pending) job
        var ctx = _fixture.CreateContext();
        var nextJobId = Guid.NewGuid();
        var pastTime = DateTime.UtcNow.AddMinutes(-5);

        ctx.Set<Job>().Add(new Job
        {
            Id = nextJobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued, // Still pending
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            CreateTime = pastTime,
            ScheduleTime = pastTime,
            Queue = "default",
        });
        ctx.Set<RecurringJob>().Add(new RecurringJob
        {
            Name = "skip-test",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            Cron = "* * * * *",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            NextExecution = pastTime,
            NextJobId = nextJobId,
        });
        await ctx.SaveChangesAsync();

        var jobCountBefore = await _fixture.CreateContext().Set<Job>().CountAsync();

        // Act
        var schedCtx = _fixture.CreateContext();
        var count = await RecurringJobSchedulerTask<TestContext>.ScheduleRecurringJobs(schedCtx);

        // Assert — should skip because the pending job still exists
        count.ShouldBe(0);

        var jobCountAfter = await _fixture.CreateContext().Set<Job>().CountAsync();
        jobCountAfter.ShouldBe(jobCountBefore);
    }

    [Fact]
    public async Task ScheduleRecurringJobs_MultipleDueJobs_SchedulesAll()
    {
        // Arrange — insert 3 recurring jobs with past NextExecution, each with a completed next job
        var ctx = _fixture.CreateContext();
        var pastTime = DateTime.UtcNow.AddMinutes(-5);

        for (var i = 0; i < 3; i++)
        {
            var nextJobId = Guid.NewGuid();
            ctx.Set<Job>().Add(new Job
            {
                Id = nextJobId,
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                Type = typeof(UnitRequest).AssemblyQualifiedName,
                Message = JsonSerializer.Serialize(new UnitRequest()),
                CreateTime = pastTime,
                ScheduleTime = pastTime,
                Queue = "default",
            });
            ctx.Set<RecurringJob>().Add(new RecurringJob
            {
                Name = $"multi-test-{i}",
                Type = typeof(UnitRequest).AssemblyQualifiedName,
                Message = JsonSerializer.Serialize(new UnitRequest()),
                Cron = "* * * * *",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                NextExecution = pastTime,
                NextJobId = nextJobId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var schedCtx = _fixture.CreateContext();
        var count = await RecurringJobSchedulerTask<TestContext>.ScheduleRecurringJobs(schedCtx);

        // Assert
        count.ShouldBe(3);
    }

    [Fact]
    public async Task ScheduleRecurringJobs_UpdatesNextExecution()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var nextJobId = Guid.NewGuid();
        var pastTime = DateTime.UtcNow.AddMinutes(-5);

        ctx.Set<Job>().Add(new Job
        {
            Id = nextJobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            CreateTime = pastTime,
            ScheduleTime = pastTime,
            Queue = "default",
        });
        ctx.Set<RecurringJob>().Add(new RecurringJob
        {
            Name = "next-exec-test",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            Cron = "* * * * *",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            NextExecution = pastTime,
            NextJobId = nextJobId,
        });
        await ctx.SaveChangesAsync();

        // Act
        var schedCtx = _fixture.CreateContext();
        await RecurringJobSchedulerTask<TestContext>.ScheduleRecurringJobs(schedCtx);

        // Assert — NextExecution should be updated to a future time
        var readCtx = _fixture.CreateContext();
        var rj = await readCtx.Set<RecurringJob>().FirstAsync(r => r.Name == "next-exec-test");
        rj.NextExecution.ShouldNotBeNull();
        rj.NextExecution.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task ScheduleRecurringJobs_UpdatesLastExecution()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var nextJobId = Guid.NewGuid();
        var pastTime = DateTime.UtcNow.AddMinutes(-5);

        ctx.Set<Job>().Add(new Job
        {
            Id = nextJobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            CreateTime = pastTime,
            ScheduleTime = pastTime,
            Queue = "default",
        });
        ctx.Set<RecurringJob>().Add(new RecurringJob
        {
            Name = "last-exec-test",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            Cron = "* * * * *",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            NextExecution = pastTime,
            NextJobId = nextJobId,
        });
        await ctx.SaveChangesAsync();

        // Act
        var schedCtx = _fixture.CreateContext();
        await RecurringJobSchedulerTask<TestContext>.ScheduleRecurringJobs(schedCtx);

        // Assert — LastExecution should be set (was the previous NextExecution)
        var readCtx = _fixture.CreateContext();
        var rj = await readCtx.Set<RecurringJob>().FirstAsync(r => r.Name == "last-exec-test");
        rj.LastExecution.ShouldNotBeNull();
        rj.LastExecution.Value.ShouldBe(pastTime, TimeSpan.FromSeconds(1));
    }
}

[Collection("PostgreSql")]
public class RecurringJobEdgeCaseTests_PostgreSql : RecurringJobEdgeCaseTestsBase
{
    public RecurringJobEdgeCaseTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class RecurringJobEdgeCaseTests_SqlServer : RecurringJobEdgeCaseTestsBase
{
    public RecurringJobEdgeCaseTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
