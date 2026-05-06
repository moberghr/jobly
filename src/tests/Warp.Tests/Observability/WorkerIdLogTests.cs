using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using Warp.Core;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Services;
using Warp.Tests.Fixtures;

namespace Warp.Tests.Observability;

[GenerateDatabaseTests]
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
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = Warp.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.DeleteJob(jobId);

        // Assert
        var logs = await _fixture.CreateContext().Set<JobLog>().Where(x => x.JobId == jobId).ToListAsync(Xunit.TestContext.Current.CancellationToken);
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
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = Warp.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.RequeueJob(jobId);

        // Assert
        var logs = await _fixture.CreateContext().Set<JobLog>().Where(x => x.JobId == jobId).ToListAsync(Xunit.TestContext.Current.CancellationToken);
        var requeuedLog = logs.ShouldHaveSingleItem();
        requeuedLog.EventType.ShouldBe("Requeued");
        requeuedLog.WorkerId.ShouldBeNull();
    }
}
