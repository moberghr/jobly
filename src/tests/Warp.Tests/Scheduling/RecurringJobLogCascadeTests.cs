using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Worker.Services;

namespace Warp.Tests.Scheduling;

[GenerateDatabaseTests]
public abstract class RecurringJobLogCascadeTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected RecurringJobLogCascadeTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task WhenJobIsDeleted_RecurringJobLog_JobIdSetToNull()
    {
        // Arrange: create a recurring job, a job, and a log linking them
        var ctx = _fixture.CreateContext();
        var rj = new RecurringJob { Name = "cascade-test", Cron = "* * * * *", CreatedAt = DateTime.UtcNow };
        ctx.Set<RecurringJob>().Add(rj);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddHours(-1),
        });
        ctx.Set<RecurringJobLog>().Add(new RecurringJobLog
        {
            RecurringJobId = rj.Id,
            JobId = jobId,
            CreatedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act: delete the job via expiration cleanup (simulates real cleanup)
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert: log entry survives with JobId set to null
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync([jobId], Xunit.TestContext.Current.CancellationToken);
        job.ShouldBeNull("Job should be cleaned up");

        var log = await readCtx.Set<RecurringJobLog>()
            .FirstOrDefaultAsync(l => l.RecurringJobId == rj.Id, Xunit.TestContext.Current.CancellationToken);
        log.ShouldNotBeNull("Log entry should survive");
        log.JobId.ShouldBeNull("JobId should be set to null by cascade");
    }

    [TimedFact]
    public async Task WhenJobExists_RecurringJobLog_JobIdIsSet()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var rj = new RecurringJob { Name = "exists-test", Cron = "* * * * *", CreatedAt = DateTime.UtcNow };
        ctx.Set<RecurringJob>().Add(rj);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

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
        ctx.Set<RecurringJobLog>().Add(new RecurringJobLog
        {
            RecurringJobId = rj.Id,
            JobId = jobId,
            CreatedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert: log entry has JobId set
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<RecurringJobLog>()
            .FirstOrDefaultAsync(l => l.RecurringJobId == rj.Id, Xunit.TestContext.Current.CancellationToken);
        log.ShouldNotBeNull();
        log.JobId.ShouldBe(jobId);
    }
}
