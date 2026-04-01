using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class RequeueEdgeCaseTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected RequeueEdgeCaseTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RequeueJob_AlreadyEnqueued_NoOpReturnsEarly()
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
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext());
        await svc.RequeueJob(jobId);

        // Assert — state should remain Enqueued, and no Requeued log should exist
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);

        var logs = await readCtx.Set<JobLog>().Where(l => l.JobId == jobId).ToListAsync();
        logs.ShouldNotContain(l => l.EventType == "Requeued");
    }

    [Fact]
    public async Task RequeueJob_DeletedJob_RequeuesSuccessfully()
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
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext());
        await svc.RequeueJob(jobId);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
        job.ExpireAt.ShouldBeNull();
    }

    [Fact]
    public async Task RequeueJob_ParentNotFound_StillRequeues()
    {
        // Arrange — child job with ParentJobId pointing to non-existent parent
        // We cannot set a FK to a non-existent row, so we create a real parent, then delete it
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 1,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = childId,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });
        await ctx.SaveChangesAsync();

        // Act — requeue should succeed even though parent exists (and handle it gracefully)
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext());
        await svc.RequeueJob(childId);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == childId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task RequeueJob_NonExistentJob_Throws()
    {
        // Act & Assert
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext());
        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await svc.RequeueJob(Guid.NewGuid()));

        ex.Message.ShouldContain("Job not found");
    }

    [Fact]
    public async Task DeleteJob_NonExistentJob_Throws()
    {
        // Act & Assert
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext());
        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await svc.DeleteJob(Guid.NewGuid()));

        ex.Message.ShouldContain("Job not found");
    }
}

[Collection("PostgreSql")]
public class RequeueEdgeCaseTests_PostgreSql : RequeueEdgeCaseTestsBase
{
    public RequeueEdgeCaseTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class RequeueEdgeCaseTests_SqlServer : RequeueEdgeCaseTestsBase
{
    public RequeueEdgeCaseTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
