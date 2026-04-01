using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class StatsTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected StatsTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetStatsHistory_ReturnsHourlyData()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var hourKey = $"stats:succeeded:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        ctx.Set<Statistic>().Add(new Statistic { Key = hourKey, Value = 5 });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var history = await svc.GetStatsHistory(24);

        // Assert
        history.Count.ShouldBeGreaterThanOrEqualTo(1);
        history.ShouldContain(p => p.Succeeded >= 5);
    }

    [Fact]
    public async Task GetJoblyStatus_ReturnsCorrectCounts()
    {
        // Arrange
        var ctx = _fixture.CreateContext();

        for (var i = 0; i < 3; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Enqueued,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
            });
        }

        for (var i = 0; i < 2; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Failed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
            });
        }

        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var status = await svc.GetJoblyStatus();

        // Assert
        status.Created.ShouldBe(3);
        status.Failed.ShouldBe(2);
        status.Completed.ShouldBe(1);
        status.Total.ShouldBe(6);
    }

    [Fact]
    public async Task GetServers_ReturnsRegisteredServers()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        var worker1Id = Guid.NewGuid();
        var worker2Id = Guid.NewGuid();

        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            ServiceCount = 2,
        });
        ctx.Set<Jobly.Core.Data.Entities.Worker>().Add(new Jobly.Core.Data.Entities.Worker
        {
            Id = worker1Id,
            ServerId = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });
        ctx.Set<Jobly.Core.Data.Entities.Worker>().Add(new Jobly.Core.Data.Entities.Worker
        {
            Id = worker2Id,
            ServerId = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var servers = await svc.GetServers();

        // Assert
        servers.Count.ShouldBe(1);
        servers[0].Id.ShouldBe(serverId);
        servers[0].Workers.Count.ShouldBe(2);
    }
}

[Collection("PostgreSql")]
public class StatsTests_PostgreSql : StatsTestsBase
{
    public StatsTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class StatsTests_SqlServer : StatsTestsBase
{
    public StatsTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
