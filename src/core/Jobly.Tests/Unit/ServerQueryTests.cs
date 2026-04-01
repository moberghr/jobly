using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class ServerQueryTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected ServerQueryTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetServerLogs_ReturnsPaginatedLogs()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();

        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            ServiceCount = 1,
        });

        var task = new ServerTask
        {
            ServerId = serverId,
            TaskName = "StaleJobRecovery",
            IntervalSeconds = 60,
        };
        ctx.Set<ServerTask>().Add(task);
        await ctx.SaveChangesAsync();

        // Insert 5 log entries
        for (var i = 0; i < 5; i++)
        {
            ctx.Set<ServerLog>().Add(new ServerLog
            {
                ServerId = serverId,
                ServerTaskId = task.Id,
                Status = "Success",
                Message = $"Log entry {i}",
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext());
        var request = new BaseListRequest { Page = 0, PageSize = 3 };
        var logs = await svc.GetServerLogs(serverId, request);

        // Assert
        logs.TotalCount.ShouldBe(5);
        logs.Items.Count.ShouldBe(3);
        logs.PageCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetServerLogs_FilteredByTaskName()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();

        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            ServiceCount = 1,
        });

        var task1 = new ServerTask
        {
            ServerId = serverId,
            TaskName = "StaleJobRecovery",
            IntervalSeconds = 60,
        };
        var task2 = new ServerTask
        {
            ServerId = serverId,
            TaskName = "ExpirationCleanup",
            IntervalSeconds = 120,
        };
        ctx.Set<ServerTask>().Add(task1);
        ctx.Set<ServerTask>().Add(task2);
        await ctx.SaveChangesAsync();

        // Insert logs for task1
        for (var i = 0; i < 3; i++)
        {
            ctx.Set<ServerLog>().Add(new ServerLog
            {
                ServerId = serverId,
                ServerTaskId = task1.Id,
                Status = "Success",
                Message = $"Recovery log {i}",
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
            });
        }

        // Insert logs for task2
        for (var i = 0; i < 2; i++)
        {
            ctx.Set<ServerLog>().Add(new ServerLog
            {
                ServerId = serverId,
                ServerTaskId = task2.Id,
                Status = "Success",
                Message = $"Cleanup log {i}",
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext());
        var request = new BaseListRequest { Page = 0, PageSize = 20 };
        var logs = await svc.GetServerLogs(serverId, request, taskName: "StaleJobRecovery");

        // Assert
        logs.TotalCount.ShouldBe(3);
        logs.Items.ShouldAllBe(l => string.Equals(l.TaskName, "StaleJobRecovery", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetServerTaskSummaries_ReturnsRegisteredTasks()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();

        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            ServiceCount = 1,
        });

        ctx.Set<ServerTask>().Add(new ServerTask
        {
            ServerId = serverId,
            TaskName = "StaleJobRecovery",
            IntervalSeconds = 60,
            LastStatus = "Success",
            LastMessage = "Requeued 2 stale jobs",
            LastRun = DateTime.UtcNow.AddMinutes(-1),
            LastDurationMs = 42.5,
        });
        ctx.Set<ServerTask>().Add(new ServerTask
        {
            ServerId = serverId,
            TaskName = "ExpirationCleanup",
            IntervalSeconds = 120,
            LastStatus = "Success",
            LastMessage = "Cleaned 0 expired jobs",
            LastRun = DateTime.UtcNow.AddMinutes(-2),
            LastDurationMs = 15.3,
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext());
        var summaries = await svc.GetServerTaskSummaries(serverId);

        // Assert
        summaries.Count.ShouldBe(2);
        summaries.ShouldContain(s => string.Equals(s.TaskName, "StaleJobRecovery", StringComparison.Ordinal));
        summaries.ShouldContain(s => string.Equals(s.TaskName, "ExpirationCleanup", StringComparison.Ordinal));

        var recovery = summaries.First(s => string.Equals(s.TaskName, "StaleJobRecovery", StringComparison.Ordinal));
        recovery.IntervalSeconds.ShouldBe(60);
        recovery.LastStatus.ShouldBe("Success");
        recovery.LastMessage.ShouldBe("Requeued 2 stale jobs");
        recovery.LastDurationMs.ShouldBe(42.5);
    }
}

[Collection("PostgreSql")]
public class ServerQueryTests_PostgreSql : ServerQueryTestsBase
{
    public ServerQueryTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class ServerQueryTests_SqlServer : ServerQueryTestsBase
{
    public ServerQueryTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
