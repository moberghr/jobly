using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Worker.Services;

namespace Warp.Tests.Scheduling;

[GenerateDatabaseTests]
public abstract class ExpirationEdgeCaseTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private static readonly Guid ServerId = Guid.NewGuid();

    protected ExpirationEdgeCaseTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();

        // Insert a server so FK constraints on ServerTask and ServerLog are satisfied
        var ctx = _fixture.CreateContext();
        ctx.Set<Server>().Add(new Server
        {
            Id = ServerId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            ServiceCount = 1,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Inserts an expired job so that RunCleanup has work to do and triggers the stats/log cleanup path.
    /// </summary>
    private static async Task InsertExpiredJob(TestContext ctx)
    {
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow.AddDays(-2),
            ScheduleTime = DateTime.UtcNow.AddDays(-2),
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddHours(-1),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
    }

    [TimedFact]
    public async Task RunCleanup_OldHourlyStats_Deleted()
    {
        // Arrange — use stats:failed: prefix because the cleanup uses a lexicographic comparison
        // against "stats:failed:{cutoff}" which only matches stats:failed: keys
        var ctx = _fixture.CreateContext();
        await InsertExpiredJob(ctx);

        const string oldKey = "stats:failed:2020-01-01-10";
        ctx.Set<Statistic>().Add(new Statistic { Key = oldKey, Value = 5 });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var stat = await readCtx.Set<Statistic>().FirstOrDefaultAsync(s => s.Key == oldKey, Xunit.TestContext.Current.CancellationToken);
        stat.ShouldBeNull();
    }

    [TimedFact]
    public async Task RunCleanup_RecentHourlyStats_Kept()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        await InsertExpiredJob(ctx);

        var recentKey = $"stats:failed:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        ctx.Set<Statistic>().Add(new Statistic { Key = recentKey, Value = 7 });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var stat = await readCtx.Set<Statistic>().FirstOrDefaultAsync(s => s.Key == recentKey, Xunit.TestContext.Current.CancellationToken);
        stat.ShouldNotBeNull();
        stat.Value.ShouldBe(7);
    }

    [TimedFact]
    public async Task RunCleanup_OldServerLogs_DeletedBasedOnTaskInterval()
    {
        // Arrange — insert a ServerTask with 60s interval, retention = 60 * 300 = 18000 seconds
        var ctx = _fixture.CreateContext();
        await InsertExpiredJob(ctx);

        var serverTask = new ServerTask
        {
            ServerId = ServerId,
            TaskName = "TestTask",
            IntervalSeconds = 60,
        };
        ctx.Set<ServerTask>().Add(serverTask);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Insert an old server log well beyond the retention window
        var oldLog = new ServerLog
        {
            ServerId = ServerId,
            ServerTaskId = serverTask.Id,
            Status = "Success",
            Message = "Old log",
            Timestamp = DateTime.UtcNow.AddSeconds(-20000), // Older than 18000s retention
        };
        ctx.Set<ServerLog>().Add(oldLog);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
        var logId = oldLog.Id;

        // Act
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<ServerLog>().FirstOrDefaultAsync(l => l.Id == logId, Xunit.TestContext.Current.CancellationToken);
        log.ShouldBeNull();
    }

    [TimedFact]
    public async Task RunCleanup_RecentServerLogs_Kept()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        await InsertExpiredJob(ctx);

        var serverTask = new ServerTask
        {
            ServerId = ServerId,
            TaskName = "TestTask",
            IntervalSeconds = 60,
        };
        ctx.Set<ServerTask>().Add(serverTask);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Insert a recent server log
        var recentLog = new ServerLog
        {
            ServerId = ServerId,
            ServerTaskId = serverTask.Id,
            Status = "Success",
            Message = "Recent log",
            Timestamp = DateTime.UtcNow.AddSeconds(-10), // Very recent
        };
        ctx.Set<ServerLog>().Add(recentLog);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
        var logId = recentLog.Id;

        // Act
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<ServerLog>().FirstOrDefaultAsync(l => l.Id == logId, Xunit.TestContext.Current.CancellationToken);
        log.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task RunCleanup_JobExpiringExactlyNow_NotDeleted()
    {
        // Arrange — job with ExpireAt == now should NOT be deleted (strict < comparison)
        var fixedTime = DateTime.UtcNow.AddMinutes(10);
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = fixedTime,
            ScheduleTime = fixedTime,
            Queue = "default",
            ExpireAt = fixedTime,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act — use a FakeTimeProvider that returns the same fixedTime
        var tp = new FakeTimeProvider(fixedTime);
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, tp).RunCleanupAsync(CancellationToken.None);

        // Assert — job should still exist (ExpireAt is NOT < now, it's equal)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync([jobId], Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task RunCleanup_ParentWithUnexpiredChild_NotDeleted()
    {
        // Arrange — parent has expired, but child hasn't yet → parent must be kept (FK safety)
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow.AddDays(-2),
            ScheduleTime = DateTime.UtcNow.AddDays(-2),
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddHours(-1),
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = childId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
            ExpireAt = DateTime.UtcNow.AddHours(1),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert — parent should still exist
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync([parentId], Xunit.TestContext.Current.CancellationToken);
        parent.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task RunCleanup_NoExpiredJobs_ReturnsZero()
    {
        // Arrange — no expired jobs
        var ctx = _fixture.CreateContext();
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddHours(1),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var cleanCtx = _fixture.CreateContext();
        var deleted = await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert
        deleted.ShouldBe(0);
    }

    [TimedFact]
    public async Task RunCleanup_RecentOrphanedServerLogs_Kept()
    {
        // Arrange — orphaned log (no TaskId) less than 1 day old should be kept
        var ctx = _fixture.CreateContext();
        await InsertExpiredJob(ctx);

        var recentOrphanedLog = new ServerLog
        {
            ServerId = ServerId,
            ServerTaskId = null,
            Status = "Info",
            Message = "Recent orphaned log",
            Timestamp = DateTime.UtcNow.AddHours(-1),
        };
        ctx.Set<ServerLog>().Add(recentOrphanedLog);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
        var logId = recentOrphanedLog.Id;

        // Act
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<ServerLog>().FirstOrDefaultAsync(x => x.Id == logId, Xunit.TestContext.Current.CancellationToken);
        log.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task RunCleanup_ServerTaskWithNullInterval_UsesDefaultRetention()
    {
        // Arrange — task with null IntervalSeconds should use default (60s * 300 = 18000s)
        var ctx = _fixture.CreateContext();
        await InsertExpiredJob(ctx);

        var serverTask = new ServerTask
        {
            ServerId = ServerId,
            TaskName = "NullIntervalTask",
            IntervalSeconds = null,
        };
        ctx.Set<ServerTask>().Add(serverTask);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Insert a log older than default retention (18000s ≈ 5 hours)
        var oldLog = new ServerLog
        {
            ServerId = ServerId,
            ServerTaskId = serverTask.Id,
            Status = "Success",
            Message = "Old log",
            Timestamp = DateTime.UtcNow.AddHours(-6),
        };
        ctx.Set<ServerLog>().Add(oldLog);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
        var logId = oldLog.Id;

        // Act
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<ServerLog>().FirstOrDefaultAsync(x => x.Id == logId, Xunit.TestContext.Current.CancellationToken);
        log.ShouldBeNull();
    }

    [TimedFact]
    public async Task RunCleanup_OrphanedServerLogs_DeletedAfterOneDay()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        await InsertExpiredJob(ctx);

        // Insert an orphaned server log (no ServerTaskId) older than 1 day
        var orphanedLog = new ServerLog
        {
            ServerId = ServerId,
            ServerTaskId = null,
            Status = "Info",
            Message = "Orphaned log",
            Timestamp = DateTime.UtcNow.AddDays(-2),
        };
        ctx.Set<ServerLog>().Add(orphanedLog);
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
        var logId = orphanedLog.Id;

        // Act
        var cleanCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<ServerLog>().FirstOrDefaultAsync(l => l.Id == logId, Xunit.TestContext.Current.CancellationToken);
        log.ShouldBeNull();
    }

    /// <summary>
    /// BUG: Expiration cleanup fails with FK violation when parent job has ExpireAt
    /// but child jobs still reference it via ParentJobId.
    /// </summary>
    [TimedFact]
    public async Task ExpirationCleanup_ParentWithChildren_DoesNotThrowFkViolation()
    {
        // Arrange: parent with ExpireAt in the past, child referencing it
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
            ExpireAt = DateTime.UtcNow.AddHours(-1), // expired
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = childId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
            ExpireAt = DateTime.UtcNow.AddHours(-1), // also expired
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act: should not throw FK violation
        var cleanCtx = _fixture.CreateContext();
        await Should.NotThrowAsync(async () =>
            await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None));

        // Assert: both should be deleted
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync([parentId], Xunit.TestContext.Current.CancellationToken);
        var child = await readCtx.Set<Job>().FindAsync([childId], Xunit.TestContext.Current.CancellationToken);
        parent.ShouldBeNull("Parent should be cleaned up");
        child.ShouldBeNull("Child should be cleaned up");
    }

    /// <summary>
    /// BUG: Expiration cleanup fails when parent is expired but child is not yet expired.
    /// Parent can't be deleted because child still references it.
    /// </summary>
    [TimedFact]
    public async Task ExpirationCleanup_ParentExpiredChildNot_DoesNotThrowFkViolation()
    {
        // Arrange: parent expired, child NOT expired (still has future ExpireAt)
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
            ExpireAt = DateTime.UtcNow.AddHours(-1), // expired
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = childId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
            ExpireAt = DateTime.UtcNow.AddDays(1), // NOT expired
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act: should not throw
        var cleanCtx = _fixture.CreateContext();
        await Should.NotThrowAsync(async () =>
            await Warp.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None));

        // Assert: parent should NOT be deleted (child still references it)
        // OR both deleted together — either way no FK error
        var readCtx = _fixture.CreateContext();
        var child = await readCtx.Set<Job>().FindAsync([childId], Xunit.TestContext.Current.CancellationToken);
        if (child != null)
        {
            // Child survived — parent must also survive (FK intact)
            var parent = await readCtx.Set<Job>().FindAsync([parentId], Xunit.TestContext.Current.CancellationToken);
            parent.ShouldNotBeNull("Parent can't be deleted while child exists");
        }
    }
}

file class FakeTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
