using Jobly.Core;
using Jobly.Core.Data.Entities;
using Shouldly;

namespace Jobly.Tests.Services;

public abstract partial class ServiceTests : TestBase
{
    [Fact]
    public async Task GetServerById_ReturnsServerWithWorkers()
    {
        await EnsureServerRegistered();
        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var server = await service.GetServerById(TestUtils.TestServerId);

        server.ShouldNotBeNull();
        server.Id.ShouldBe(TestUtils.TestServerId);
        server.Workers.Count.ShouldBe(1);
        server.Workers[0].WorkerId.ShouldBe(TestUtils.TestWorkerId);
    }

    [Fact]
    public async Task GetServerById_NonExistent_ReturnsNull()
    {
        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var server = await service.GetServerById(Guid.NewGuid());
        server.ShouldBeNull();
    }

    [Fact]
    public async Task GetServerTaskSummaries_ReturnsRegisteredTasks()
    {
        await EnsureServerRegistered();
        var context = CreateContext();

        context.Set<ServerTask>().Add(new ServerTask
        {
            ServerId = TestUtils.TestServerId,
            TaskName = "TestTask",
            IntervalSeconds = 30,
            LastStatus = "Completed",
            LastMessage = "Did something",
            LastRun = DateTime.UtcNow,
            LastDurationMs = 5.0,
        });
        await context.SaveChangesAsync();

        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var tasks = await service.GetServerTaskSummaries(TestUtils.TestServerId);

        tasks.Count.ShouldBe(1);
        tasks[0].TaskName.ShouldBe("TestTask");
        tasks[0].IntervalSeconds.ShouldBe(30);
        tasks[0].LastStatus.ShouldBe("Completed");
    }

    [Fact]
    public async Task GetServerLogs_ReturnsPaginatedLogs()
    {
        await EnsureServerRegistered();
        var context = CreateContext();

        var serverTask = new ServerTask
        {
            ServerId = TestUtils.TestServerId,
            TaskName = "TestTask",
            IntervalSeconds = 10,
        };
        context.Set<ServerTask>().Add(serverTask);
        await context.SaveChangesAsync();

        for (var i = 0; i < 5; i++)
        {
            context.Set<ServerLog>().Add(new ServerLog
            {
                ServerId = TestUtils.TestServerId,
                ServerTaskId = serverTask.Id,
                Status = "Completed",
                Message = $"Run {i}",
                Timestamp = DateTime.UtcNow.AddSeconds(-i),
                DurationMs = 10.0 + i,
            });
        }

        await context.SaveChangesAsync();

        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var result = await service.GetServerLogs(TestUtils.TestServerId, new BaseListRequest { Page = 0, PageSize = 3 });

        result.TotalCount.ShouldBe(5);
        result.Items.Count.ShouldBe(3);
        result.PageCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetServerLogs_FilteredByTaskName()
    {
        await EnsureServerRegistered();
        var context = CreateContext();

        var task1 = new ServerTask { ServerId = TestUtils.TestServerId, TaskName = "TaskA", IntervalSeconds = 10 };
        var task2 = new ServerTask { ServerId = TestUtils.TestServerId, TaskName = "TaskB", IntervalSeconds = 20 };
        context.Set<ServerTask>().AddRange(task1, task2);
        await context.SaveChangesAsync();

        context.Set<ServerLog>().Add(new ServerLog { ServerId = TestUtils.TestServerId, ServerTaskId = task1.Id, Status = "Completed", Timestamp = DateTime.UtcNow });
        context.Set<ServerLog>().Add(new ServerLog { ServerId = TestUtils.TestServerId, ServerTaskId = task2.Id, Status = "Completed", Timestamp = DateTime.UtcNow });
        context.Set<ServerLog>().Add(new ServerLog { ServerId = TestUtils.TestServerId, ServerTaskId = task1.Id, Status = "Completed", Timestamp = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var result = await service.GetServerLogs(TestUtils.TestServerId, new BaseListRequest { Page = 0, PageSize = 10 }, "TaskA");

        result.TotalCount.ShouldBe(2);
    }
}
