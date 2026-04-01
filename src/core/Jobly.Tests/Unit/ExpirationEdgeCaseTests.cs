using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class ExpirationEdgeCaseTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private static readonly Guid ServerId = Guid.NewGuid();

    protected ExpirationEdgeCaseTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
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
        await ctx.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task RunCleanup_OldHourlyStats_Deleted()
    {
        // Arrange — use stats:failed: prefix because the cleanup uses a lexicographic comparison
        // against "stats:failed:{cutoff}" which only matches stats:failed: keys
        var ctx = _fixture.CreateContext();
        await InsertExpiredJob(ctx);

        var oldKey = "stats:failed:2020-01-01-10";
        ctx.Set<Statistic>().Add(new Statistic { Key = oldKey, Value = 5 });
        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var stat = await readCtx.Set<Statistic>().FirstOrDefaultAsync(s => s.Key == oldKey);
        stat.ShouldBeNull();
    }

    [Fact]
    public async Task RunCleanup_RecentHourlyStats_Kept()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        await InsertExpiredJob(ctx);

        var recentKey = $"stats:failed:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        ctx.Set<Statistic>().Add(new Statistic { Key = recentKey, Value = 7 });
        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var stat = await readCtx.Set<Statistic>().FirstOrDefaultAsync(s => s.Key == recentKey);
        stat.ShouldNotBeNull();
        stat.Value.ShouldBe(7);
    }

    [Fact]
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
        await ctx.SaveChangesAsync();

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
        await ctx.SaveChangesAsync();
        var logId = oldLog.Id;

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<ServerLog>().FirstOrDefaultAsync(l => l.Id == logId);
        log.ShouldBeNull();
    }

    [Fact]
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
        await ctx.SaveChangesAsync();

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
        await ctx.SaveChangesAsync();
        var logId = recentLog.Id;

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<ServerLog>().FirstOrDefaultAsync(l => l.Id == logId);
        log.ShouldNotBeNull();
    }

    [Fact]
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
        await ctx.SaveChangesAsync();
        var logId = orphanedLog.Id;

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var log = await readCtx.Set<ServerLog>().FirstOrDefaultAsync(l => l.Id == logId);
        log.ShouldBeNull();
    }
}

[Collection("PostgreSql")]
public class ExpirationEdgeCaseTests_PostgreSql : ExpirationEdgeCaseTestsBase
{
    public ExpirationEdgeCaseTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class ExpirationEdgeCaseTests_SqlServer : ExpirationEdgeCaseTestsBase
{
    public ExpirationEdgeCaseTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
