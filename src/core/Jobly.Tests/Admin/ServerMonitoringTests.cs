using Jobly.Core.Data.Entities;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Shouldly;

namespace Jobly.Tests.Admin;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class ServerMonitoringTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected ServerMonitoringTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task GetServers_ReturnsServerWithWorkers()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();

        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            ServiceCount = 2,
        });
        ctx.Set<Jobly.Core.Data.Entities.Worker>().Add(new Jobly.Core.Data.Entities.Worker
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });
        ctx.Set<Jobly.Core.Data.Entities.Worker>().Add(new Jobly.Core.Data.Entities.Worker
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var servers = await svc.GetServers();

        // Assert
        servers.Count.ShouldBe(1);
        servers[0].Id.ShouldBe(serverId);
        servers[0].Workers.Count.ShouldBe(2);
    }

    [TimedFact]
    public async Task GetServerById_ReturnsServerDetail()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();

        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            ServiceCount = 1,
        });
        ctx.Set<Jobly.Core.Data.Entities.Worker>().Add(new Jobly.Core.Data.Entities.Worker
        {
            Id = workerId,
            ServerId = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var server = await svc.GetServerById(serverId);

        // Assert
        server.ShouldNotBeNull();
        server.Id.ShouldBe(serverId);
        server.ServiceCount.ShouldBe(1);
        server.Workers.Count.ShouldBe(1);
        server.Workers[0].WorkerId.ShouldBe(workerId);
    }

    [TimedFact]
    public async Task GetServerById_NonExistent_ReturnsNull()
    {
        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var server = await svc.GetServerById(Guid.NewGuid());

        // Assert
        server.ShouldBeNull();
    }
}
