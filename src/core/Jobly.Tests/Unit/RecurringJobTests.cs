using System.Text.Json;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class RecurringJobTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected RecurringJobTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddOrUpdateRecurringJob_CreatesRecurringJobInDb()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = new RecurringJobPublisher<TestContext>(ctx, TimeProvider.System);

        // Act
        await publisher.AddOrUpdateRecurringJob(new UnitRequest(), "test-recurring", "* * * * *");

        // Assert
        var readCtx = _fixture.CreateContext();
        var recurringJob = await readCtx.Set<RecurringJob>()
            .FirstOrDefaultAsync(r => r.Name == "test-recurring");

        recurringJob.ShouldNotBeNull();
        recurringJob.Cron.ShouldBe("* * * * *");
        recurringJob.Name.ShouldBe("test-recurring");
        recurringJob.Queue.ShouldBe("default");
    }

    [Fact]
    public async Task GetRecurringJobs_ReturnsPaginated()
    {
        // Arrange
        for (var i = 0; i < 3; i++)
        {
            var publisher = new RecurringJobPublisher<TestContext>(_fixture.CreateContext(), TimeProvider.System);
            await publisher.AddOrUpdateRecurringJob(new UnitRequest(), $"recurring-{i}", "* * * * *");
        }

        // Act
        var svc = new RecurringJobService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.GetRecurringJobs(new BaseListRequest { Page = 0, PageSize = 20 });

        // Assert
        result.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task GetRecurringJobById_ReturnsDetail()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = new RecurringJobPublisher<TestContext>(ctx, TimeProvider.System);
        await publisher.AddOrUpdateRecurringJob(new UnitRequest(), "detail-test", "*/5 * * * *");

        var readCtx = _fixture.CreateContext();
        var rj = await readCtx.Set<RecurringJob>().FirstAsync(r => r.Name == "detail-test");

        // Act
        var svc = new RecurringJobService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var detail = await svc.GetRecurringJobById(rj.Id);

        // Assert
        detail.ShouldNotBeNull();
        detail.Name.ShouldBe("detail-test");
        detail.Cron.ShouldBe("*/5 * * * *");
    }

    [Fact]
    public async Task DeleteRecurringJob_RemovesFromDb()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = new RecurringJobPublisher<TestContext>(ctx, TimeProvider.System);
        await publisher.AddOrUpdateRecurringJob(new UnitRequest(), "to-delete", "* * * * *");

        var readCtx = _fixture.CreateContext();
        var rj = await readCtx.Set<RecurringJob>().FirstAsync(r => r.Name == "to-delete");
        var rjId = rj.Id;

        // Remove RecurringJobLog entries so FK won't block delete
        var detachCtx = _fixture.CreateContext();
        await detachCtx.Set<RecurringJobLog>()
            .Where(l => l.RecurringJobId == rjId)
            .ExecuteDeleteAsync();

        // Act
        var svc = new RecurringJobService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        await svc.DeleteRecurringJob(rjId);

        // Assert
        var verifyCtx = _fixture.CreateContext();
        var deleted = await verifyCtx.Set<RecurringJob>().FirstOrDefaultAsync(r => r.Id == rjId);
        deleted.ShouldBeNull();
    }

    [Fact]
    public async Task TriggerRecurringJob_CreatesJob()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = new RecurringJobPublisher<TestContext>(ctx, TimeProvider.System);
        await publisher.AddOrUpdateRecurringJob(new UnitRequest(), "trigger-test", "* * * * *");

        var readCtx = _fixture.CreateContext();
        var rj = await readCtx.Set<RecurringJob>().FirstAsync(r => r.Name == "trigger-test");

        var jobCountBefore = await _fixture.CreateContext().Set<RecurringJobLog>()
            .Where(l => l.RecurringJobId == rj.Id)
            .CountAsync();

        // Act
        var svc = new RecurringJobService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        await svc.TriggerRecurringJob(rj.Id);

        // Assert
        var jobCountAfter = await _fixture.CreateContext().Set<RecurringJobLog>()
            .Where(l => l.RecurringJobId == rj.Id)
            .CountAsync();

        jobCountAfter.ShouldBe(jobCountBefore + 1);
    }

    [Fact]
    public async Task RecurringJobScheduler_CreatesJobWhenDue()
    {
        // Arrange — create a recurring job with NextExecution in the past
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
        var recurringJob = new RecurringJob
        {
            Name = "scheduler-test",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            Cron = "* * * * *",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            NextExecution = pastTime,
        };
        ctx.Set<RecurringJob>().Add(recurringJob);
        await ctx.SaveChangesAsync();

        ctx.Set<RecurringJobLog>().Add(new RecurringJobLog
        {
            RecurringJobId = recurringJob.Id,
            JobId = nextJobId,
            CreatedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var jobCountBefore = await _fixture.CreateContext().Set<Job>().CountAsync();

        // Act
        var schedCtx = _fixture.CreateContext();
        var count = await RecurringJobSchedulerTask<TestContext>.ScheduleRecurringJobs<TestContext>(schedCtx, TimeProvider.System);

        // Assert
        count.ShouldBeGreaterThanOrEqualTo(1);

        var jobCountAfter = await _fixture.CreateContext().Set<Job>().CountAsync();
        jobCountAfter.ShouldBeGreaterThan(jobCountBefore);
    }
}

[Collection("PostgreSql")]
public class RecurringJobTests_PostgreSql : RecurringJobTestsBase
{
    public RecurringJobTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class RecurringJobTests_SqlServer : RecurringJobTestsBase
{
    public RecurringJobTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
