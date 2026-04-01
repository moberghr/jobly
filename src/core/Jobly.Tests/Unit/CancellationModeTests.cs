using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class CancellationModeTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected CancellationModeTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DeleteJob_WhenProcessing_SetsCancellationModeGraceful()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
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
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Processing);
        job.CancellationMode.ShouldBe(CancellationMode.Graceful);
        job.ExpireAt.ShouldBeNull();

        var logs = await readCtx.Set<JobLog>().Where(l => l.JobId == jobId).ToListAsync();
        logs.ShouldContain(l => l.EventType == "CancellationRequested");
    }

    [Fact]
    public async Task DeleteJob_WhenNotProcessing_SetsStateToDeleted()
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
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Deleted);
        job.CancellationMode.ShouldBe(CancellationMode.None);
        job.ExpireAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DeleteJob_WhenFailed_SetsStateToDeleted()
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
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Deleted);
        job.CancellationMode.ShouldBe(CancellationMode.None);
        job.ExpireAt.ShouldNotBeNull();
    }
}

[Collection("PostgreSql")]
public class CancellationModeTests_PostgreSql : CancellationModeTestsBase
{
    public CancellationModeTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class CancellationModeTests_SqlServer : CancellationModeTestsBase
{
    public CancellationModeTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
