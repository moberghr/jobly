using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class JobQueryServiceTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected JobQueryServiceTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetJobsList_ReturnsJobsByState()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 3; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
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
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetJobsList(new BaseListRequest { Page = 0, PageSize = 20 }, State.Completed);

        // Assert
        result.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task GetJobById_ReturnsJobWithLogs()
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

        for (var i = 0; i < 3; i++)
        {
            ctx.Set<JobLog>().Add(new JobLog
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                EventType = "Created",
                Timestamp = DateTime.UtcNow.AddSeconds(i),
                Level = "Information",
                Message = $"Log entry {i}",
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetJobById(jobId);

        // Assert
        result.ShouldNotBeNull();
        result.Logs.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetJobById_NonExistent_ReturnsNull()
    {
        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetJobById(Guid.NewGuid());

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetJobById_WithChildren_ReturnsChildCount()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });

        for (var i = 0; i < 2; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Enqueued,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ParentJobId = parentId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetJobById(parentId);

        // Assert
        result.ShouldNotBeNull();
        result.ChildJobCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetJobById_WithSiblings_ReturnsSiblingCount()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Message,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });

        var child1Id = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = child1Id,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });

        for (var i = 0; i < 2; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Enqueued,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ParentJobId = parentId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetJobById(child1Id);

        // Assert
        result.ShouldNotBeNull();
        result.SiblingJobCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetScheduledJobs_ReturnsFutureScheduledOnly()
    {
        // Arrange
        var ctx = _fixture.CreateContext();

        // Future-scheduled job
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddHours(2),
            Queue = "default",
        });

        // Past-scheduled job (should not show in scheduled)
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddHours(-1),
            Queue = "default",
        });

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetScheduledJobs(new BaseListRequest { Page = 0, PageSize = 20 });

        // Assert
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetAwaitingJobs_ReturnsOnlyAwaiting()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetAwaitingJobs(new BaseListRequest { Page = 0, PageSize = 20 });

        // Assert
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetSiblingJobs_ReturnsOtherChildrenOfSameParent()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Message,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });

        var child1Id = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = child1Id,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });

        for (var i = 0; i < 2; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Enqueued,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ParentJobId = parentId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetSiblingJobs(child1Id, new BaseListRequest { Page = 0, PageSize = 20 });

        // Assert
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetChildJobs_ReturnsDirectChildren()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });

        for (var i = 0; i < 2; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Enqueued,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ParentJobId = parentId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetChildJobs(parentId, new BaseListRequest { Page = 0, PageSize = 20 });

        // Assert
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetTraceJobs_ReturnsJobsWithSameTraceId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var traceId = Guid.NewGuid();
        var job1Id = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = job1Id,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            TraceId = traceId,
        });

        for (var i = 0; i < 2; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                TraceId = traceId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext());
        var result = await svc.GetTraceJobs(job1Id, new BaseListRequest { Page = 0, PageSize = 20 });

        // Assert
        result.TotalCount.ShouldBe(2);
    }
}

[Collection("PostgreSql")]
public class JobQueryServiceTests_PostgreSql : JobQueryServiceTestsBase
{
    public JobQueryServiceTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class JobQueryServiceTests_SqlServer : JobQueryServiceTestsBase
{
    public JobQueryServiceTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
