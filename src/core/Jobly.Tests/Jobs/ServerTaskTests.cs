using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task AggregateCounters_WithPendingCounters_AggregatesIntoStatistic()
    {
        var context = CreateContext();
        context.Set<Counter>().Add(new Counter { Key = "stats:succeeded", Value = 1 });
        context.Set<Counter>().Add(new Counter { Key = "stats:succeeded", Value = 1 });
        context.Set<Counter>().Add(new Counter { Key = "stats:failed", Value = 1 });
        await context.SaveChangesAsync();

        var count = await CounterAggregatorTask<TestContext>.AggregateCounters(CreateContext());
        count.ShouldBe(3);

        var succeeded = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        succeeded.ShouldBe(2);

        var failed = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        failed.ShouldBe(1);

        var remaining = await CreateContext().Set<Counter>().CountAsync();
        remaining.ShouldBe(0);
    }

    [Fact]
    public async Task AggregateCounters_WithNoCounters_ReturnsZero()
    {
        var count = await CounterAggregatorTask<TestContext>.AggregateCounters(CreateContext());
        count.ShouldBe(0);
    }

    [Fact]
    public async Task ExpirationCleanup_CleansOldServerLogs()
    {
        await EnsureServerRegistered();
        var context = CreateContext();

        // Create a ServerTask to link logs to
        var serverTask = new ServerTask
        {
            ServerId = TestUtils.TestServerId,
            TaskName = "TestTask",
            IntervalSeconds = 10,
        };
        context.Set<ServerTask>().Add(serverTask);
        await context.SaveChangesAsync();

        // Add old server log (8 days ago)
        context.Set<ServerLog>().Add(new ServerLog
        {
            ServerId = TestUtils.TestServerId,
            ServerTaskId = serverTask.Id,
            Status = "Completed",
            Timestamp = DateTime.UtcNow.AddDays(-8),
        });

        // Add recent server log
        context.Set<ServerLog>().Add(new ServerLog
        {
            ServerId = TestUtils.TestServerId,
            ServerTaskId = serverTask.Id,
            Status = "Completed",
            Timestamp = DateTime.UtcNow,
        });

        // Add an expired job so cleanup has something to do
        context.Set<Job>().Add(new Job
        {
            Type = "test",
            Message = "{}",
            CreateTime = DateTime.UtcNow.AddDays(-2),
            ScheduleTime = DateTime.UtcNow.AddDays(-2),
            CurrentState = State.Completed,
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddDays(-1),
        });
        await context.SaveChangesAsync();

        await ExpirationCleanupTask<TestContext>.RunCleanup(CreateContext());

        var logs = await CreateContext().Set<ServerLog>().ToListAsync();
        logs.Count.ShouldBe(1);
        logs[0].Timestamp.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(-1));
    }
}
