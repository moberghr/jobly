using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

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
        await ctx.SaveChangesAsync();

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
        await ctx.SaveChangesAsync();

        // Act: delete the job via expiration cleanup (simulates real cleanup)
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System);

        // Assert: log entry survives with JobId set to null
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldBeNull("Job should be cleaned up");

        var log = await readCtx.Set<RecurringJobLog>()
            .FirstOrDefaultAsync(l => l.RecurringJobId == rj.Id);
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
        await ctx.SaveChangesAsync();

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
        await ctx.SaveChangesAsync();

        // Assert: log entry has JobId set
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<RecurringJobLog>()
            .FirstOrDefaultAsync(l => l.RecurringJobId == rj.Id);
        log.ShouldNotBeNull();
        log.JobId.ShouldBe(jobId);
    }
}

[Collection<PostgreSqlCollection>]
[Trait("Category", "PostgreSql")]
public class RecurringJobLogCascadeTests_PostgreSql : RecurringJobLogCascadeTestsBase
{
    public RecurringJobLogCascadeTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class RecurringJobLogCascadeTests_SqlServer : RecurringJobLogCascadeTestsBase
{
    public RecurringJobLogCascadeTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
