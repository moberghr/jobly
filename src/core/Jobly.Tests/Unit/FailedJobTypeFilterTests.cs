using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Models;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class FailedJobTypeFilterTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected FailedJobTypeFilterTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetFailedJobTypeCounts_ReturnsCorrectGroupings()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 5; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                Type = "TypeA",
            });
        }

        for (var i = 0; i < 3; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                Type = "TypeB",
            });
        }

        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            Type = "TypeC",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.GetFailedJobTypeCounts();

        // Assert
        result.Count.ShouldBe(3);
        result[0].Type.ShouldBe("TypeA");
        result[0].Count.ShouldBe(5);
        result[1].Type.ShouldBe("TypeB");
        result[1].Count.ShouldBe(3);
        result[2].Type.ShouldBe("TypeC");
        result[2].Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetFailedJobTypeCounts_ExcludesNonFailedJobs()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 3; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                Type = "TypeA",
            });
        }

        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            Type = "TypeA",
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            Type = "TypeB",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.GetFailedJobTypeCounts();

        // Assert
        result.Count.ShouldBe(1);
        result[0].Type.ShouldBe("TypeA");
        result[0].Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetFailedJobsByType_ReturnsOnlyMatchingType()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 4; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                Type = "TypeA",
            });
        }

        for (var i = 0; i < 2; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                Type = "TypeB",
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.GetFailedJobsByType(new BaseListRequest { Page = 0, PageSize = 100 }, "TypeA");

        // Assert
        result.TotalCount.ShouldBe(4);
        result.Items.Count.ShouldBe(4);
        result.Items.ShouldAllBe(j => j.Type == "TypeA");
    }

    [Fact]
    public async Task DeleteFailedJobsByType_DeletesAllMatchingJobs()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 5; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                Type = "TypeA",
            });
        }

        for (var i = 0; i < 3; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                Type = "TypeB",
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.DeleteFailedJobsByType("TypeA");

        // Assert
        result.Succeeded.ShouldBe(5);

        var readCtx = _fixture.CreateContext();
        var remainingTypeA = await readCtx.Set<Job>()
            .Where(j => j.Type == "TypeA" && j.CurrentState == State.Failed)
            .CountAsync();
        remainingTypeA.ShouldBe(0);

        var remainingTypeB = await readCtx.Set<Job>()
            .Where(j => j.Type == "TypeB" && j.CurrentState == State.Failed)
            .CountAsync();
        remainingTypeB.ShouldBe(3);
    }

    [Fact]
    public async Task RequeueFailedJobsByType_RequeuesAllMatchingJobs()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var typeAIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            typeAIds.Add(id);
            ctx.Set<Job>().Add(new Job
            {
                Id = id,
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                Type = "TypeA",
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.RequeueFailedJobsByType("TypeA");

        // Assert
        result.Succeeded.ShouldBe(3);

        var readCtx = _fixture.CreateContext();
        foreach (var id in typeAIds)
        {
            var job = await readCtx.Set<Job>().FindAsync(id);
            job.ShouldNotBeNull();
            job.CurrentState.ShouldBe(State.Enqueued);
        }
    }
}

[Collection("PostgreSql")]
public class FailedJobTypeFilterTests_PostgreSql : FailedJobTypeFilterTestsBase
{
    public FailedJobTypeFilterTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class FailedJobTypeFilterTests_SqlServer : FailedJobTypeFilterTestsBase
{
    public FailedJobTypeFilterTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
