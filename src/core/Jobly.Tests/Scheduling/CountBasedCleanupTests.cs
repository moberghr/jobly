using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Scheduling;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class CountBasedCleanupTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected CountBasedCleanupTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task RunCountBasedCleanup_WhenOverThreshold_DeletesOldestByExpireAt()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 25; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ExpireAt = DateTime.UtcNow.AddHours(i),
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        var deleted = await ExpirationCleanupTask<TestContext>.RunCountBasedCleanup(cleanCtx, maxCount: 20, batchSize: 1000);

        // Assert
        deleted.ShouldBe(5);

        var readCtx = _fixture.CreateContext();
        var remaining = await readCtx.Set<Job>().CountAsync();
        remaining.ShouldBe(20);
    }

    [TimedFact]
    public async Task RunCountBasedCleanup_WhenUnderThreshold_DeletesNothing()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 15; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ExpireAt = DateTime.UtcNow.AddHours(i),
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        var deleted = await ExpirationCleanupTask<TestContext>.RunCountBasedCleanup(cleanCtx, maxCount: 20, batchSize: 1000);

        // Assert
        deleted.ShouldBe(0);

        var readCtx = _fixture.CreateContext();
        var remaining = await readCtx.Set<Job>().CountAsync();
        remaining.ShouldBe(15);
    }

    [TimedFact]
    public async Task RunCountBasedCleanup_BatchesCorrectly()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 30; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ExpireAt = DateTime.UtcNow.AddHours(i),
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        var deleted = await ExpirationCleanupTask<TestContext>.RunCountBasedCleanup(cleanCtx, maxCount: 20, batchSize: 3);

        // Assert
        deleted.ShouldBe(10);

        var readCtx = _fixture.CreateContext();
        var remaining = await readCtx.Set<Job>().ToListAsync();
        remaining.Count.ShouldBe(20);

        // The 20 remaining should be the ones with the latest ExpireAt values
        var earliestRemaining = remaining.Min(j => j.ExpireAt);
        earliestRemaining.ShouldNotBeNull();
        earliestRemaining.Value.ShouldBeGreaterThanOrEqualTo(DateTime.UtcNow.AddHours(9));
    }

    [TimedFact]
    public async Task RunCountBasedCleanup_ExcludesJobsWithNullExpireAt()
    {
        // Arrange
        var ctx = _fixture.CreateContext();

        // 25 jobs with ExpireAt
        for (var i = 0; i < 25; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ExpireAt = DateTime.UtcNow.AddHours(i),
            });
        }

        // 10 failed jobs with null ExpireAt
        var failedJobIds = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            var jobId = Guid.NewGuid();
            failedJobIds.Add(jobId);
            ctx.Set<Job>().Add(new Job
            {
                Id = jobId,
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ExpireAt = null,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        var deleted = await ExpirationCleanupTask<TestContext>.RunCountBasedCleanup(cleanCtx, maxCount: 20, batchSize: 1000);

        // Assert
        deleted.ShouldBe(5);

        var readCtx = _fixture.CreateContext();

        // All 10 null-ExpireAt jobs should be untouched
        var failedJobs = await readCtx.Set<Job>()
            .Where(j => failedJobIds.Contains(j.Id))
            .CountAsync();
        failedJobs.ShouldBe(10);

        // 20 expirable jobs remaining + 10 failed = 30 total
        var totalRemaining = await readCtx.Set<Job>().CountAsync();
        totalRemaining.ShouldBe(30);
    }

    [TimedFact]
    public async Task RunCountBasedCleanup_DeletesAssociatedJobLogs()
    {
        // Arrange — insert 25 jobs (5 over threshold of 20), with logs on the 5 oldest
        var ctx = _fixture.CreateContext();
        var jobIds = new List<Guid>();

        for (var i = 0; i < 25; i++)
        {
            var jobId = Guid.NewGuid();
            jobIds.Add(jobId);
            ctx.Set<Job>().Add(new Job
            {
                Id = jobId,
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ExpireAt = DateTime.UtcNow.AddHours(i),
            });
        }

        // Add logs to the 5 oldest jobs (indices 0-4, which will be deleted)
        for (var i = 0; i < 5; i++)
        {
            ctx.Set<JobLog>().Add(new JobLog
            {
                JobId = jobIds[i],
                EventType = "Completed",
                Timestamp = DateTime.UtcNow,
                Level = "Information",
                Message = "test",
            });
        }

        // Add a log to a job that should survive (index 20)
        ctx.Set<JobLog>().Add(new JobLog
        {
            JobId = jobIds[20],
            EventType = "Completed",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = "survivor",
        });

        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        var deleted = await ExpirationCleanupTask<TestContext>.RunCountBasedCleanup(cleanCtx, maxCount: 20, batchSize: 1000);

        // Assert
        deleted.ShouldBe(5);

        var readCtx = _fixture.CreateContext();

        // Logs for deleted jobs should be gone
        for (var i = 0; i < 5; i++)
        {
            var logs = await readCtx.Set<JobLog>().Where(l => l.JobId == jobIds[i]).CountAsync();
            logs.ShouldBe(0);
        }

        // Log for surviving job should remain
        var survivorLogs = await readCtx.Set<JobLog>().Where(l => l.JobId == jobIds[20]).CountAsync();
        survivorLogs.ShouldBe(1);
    }

    [TimedFact]
    public async Task RunCountBasedCleanup_AtExactlyMaxCount_DeletesNothing()
    {
        // Arrange — exactly at threshold should NOT trigger cleanup
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 10; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ExpireAt = DateTime.UtcNow.AddHours(i),
            });
        }

        await ctx.SaveChangesAsync();

        // Act — maxCount == actual count
        var cleanCtx = _fixture.CreateContext();
        var deleted = await ExpirationCleanupTask<TestContext>.RunCountBasedCleanup(cleanCtx, maxCount: 10, batchSize: 1000);

        // Assert
        deleted.ShouldBe(0);

        var readCtx = _fixture.CreateContext();
        var remaining = await readCtx.Set<Job>().CountAsync();
        remaining.ShouldBe(10);
    }

    /// <summary>
    /// Count-based cleanup must not delete parents whose children haven't expired.
    /// </summary>
    [TimedFact]
    public async Task CountBasedCleanup_SkipsParentsWithNonExpiredChildren()
    {
        // Arrange: 5 standalone expired jobs + 1 parent with non-expired child
        var ctx = _fixture.CreateContext();

        for (var i = 0; i < 5; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ExpireAt = DateTime.UtcNow.AddHours(-i - 1),
            });
        }

        var parentId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddHours(-1), // expired
        });
        ctx.Set<Job>().Add(new Job
        {
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
            ExpireAt = DateTime.UtcNow.AddDays(1), // NOT expired
        });

        await ctx.SaveChangesAsync();

        // Act: run count-based cleanup with threshold of 3
        var cleanCtx = _fixture.CreateContext();
        await Should.NotThrowAsync(async () =>
            await ExpirationCleanupTask<TestContext>.RunCountBasedCleanup(cleanCtx, 3, 1000));

        // Assert: parent should survive (child not expired), standalone jobs reduced to 3
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        parent.ShouldNotBeNull("Parent with non-expired child should not be deleted");

        var standaloneCount = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == null && j.Kind == JobKind.Job)
            .CountAsync();
        standaloneCount.ShouldBeLessThanOrEqualTo(3);
    }
}
