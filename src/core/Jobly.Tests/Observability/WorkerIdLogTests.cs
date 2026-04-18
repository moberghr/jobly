using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Observability;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class WorkerIdLogTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected WorkerIdLogTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task DeleteJob_LogHasNullWorkerId()
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
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));
        await svc.DeleteJob(jobId);

        // Assert
        var logs = await _fixture.CreateContext().Set<JobLog>().Where(x => x.JobId == jobId).ToListAsync();
        var deletedLog = logs.ShouldHaveSingleItem();
        deletedLog.EventType.ShouldBe("Deleted");
        deletedLog.WorkerId.ShouldBeNull();
    }

    [TimedFact]
    public async Task RequeueJob_LogHasNullWorkerId()
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
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));
        await svc.RequeueJob(jobId);

        // Assert
        var logs = await _fixture.CreateContext().Set<JobLog>().Where(x => x.JobId == jobId).ToListAsync();
        var requeuedLog = logs.ShouldHaveSingleItem();
        requeuedLog.EventType.ShouldBe("Requeued");
        requeuedLog.WorkerId.ShouldBeNull();
    }
}
