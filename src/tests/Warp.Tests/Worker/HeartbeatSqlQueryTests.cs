using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Tests.Fixtures;
using Warp.Tests.Helpers;

namespace Warp.Tests.Worker;

// Pins the contract of IWarpSqlQueries.HeartbeatAsync: a single round-trip that updates
// last_heartbeat_time / memory / cpu AND returns the server's paused_at AND every worker
// group's pause state in one shot. Heartbeat depends on the full snapshot for
// PauseStateHolder refresh, so regressions here would silently break Pause/Resume.
[GenerateDatabaseTests]
public abstract class HeartbeatSqlQueryTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected HeartbeatSqlQueryTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task HeartbeatAsync_ExistingServerNoGroups_UpdatesAndReturnsEmptyGroups()
    {
        var serverId = await SeedServerAsync(pausedAt: null);
        var queries = TestTasks.QueriesFor(_fixture.CreateContext());
        var now = DateTime.UtcNow;

        await using var ctx = _fixture.CreateContext();
        var result = await queries.HeartbeatAsync(ctx, serverId, now, memoryBytes: 1234L, cpuPercent: 42.0, default);

        result.ShouldNotBeNull();
        result.ServerPausedAt.ShouldBeNull();
        result.GroupPaused.ShouldBeEmpty();

        // The row was actually written — read it back through a fresh context.
        await using var readCtx = _fixture.CreateContext();
        var persisted = await readCtx.Set<Server>().AsNoTracking().FirstAsync(s => s.Id == serverId);
        persisted.LastHeartbeatTime.ShouldBe(now, TimeSpan.FromSeconds(1));
        persisted.MemoryWorkingSetBytes.ShouldBe(1234L);
        persisted.CpuUsagePercent.ShouldBe(42.0);
    }

    [TimedFact]
    public async Task HeartbeatAsync_PausedServer_ReturnsPausedAt()
    {
        var pausedAt = DateTime.UtcNow.AddMinutes(-5);
        var serverId = await SeedServerAsync(pausedAt: pausedAt);
        var queries = TestTasks.QueriesFor(_fixture.CreateContext());

        await using var ctx = _fixture.CreateContext();
        var result = await queries.HeartbeatAsync(ctx, serverId, DateTime.UtcNow, memoryBytes: null, cpuPercent: null, default);

        result.ShouldNotBeNull();
        result.ServerPausedAt.ShouldNotBeNull();
        result.ServerPausedAt!.Value.ShouldBe(pausedAt, TimeSpan.FromSeconds(1));
    }

    [TimedFact]
    public async Task HeartbeatAsync_MissingServer_ReturnsNull()
    {
        var queries = TestTasks.QueriesFor(_fixture.CreateContext());

        await using var ctx = _fixture.CreateContext();
        var result = await queries.HeartbeatAsync(ctx, Guid.NewGuid(), DateTime.UtcNow, memoryBytes: null, cpuPercent: null, default);

        result.ShouldBeNull();
    }

    [TimedFact]
    public async Task HeartbeatAsync_NullMemoryAndCpu_PreservesExistingValues()
    {
        var serverId = await SeedServerAsync(pausedAt: null, memoryBytes: 999L, cpuPercent: 17.5);
        var queries = TestTasks.QueriesFor(_fixture.CreateContext());

        await using var ctx = _fixture.CreateContext();
        await queries.HeartbeatAsync(ctx, serverId, DateTime.UtcNow, memoryBytes: null, cpuPercent: null, default);

        await using var readCtx = _fixture.CreateContext();
        var persisted = await readCtx.Set<Server>().AsNoTracking().FirstAsync(s => s.Id == serverId);
        persisted.MemoryWorkingSetBytes.ShouldBe(999L);
        persisted.CpuUsagePercent.ShouldBe(17.5);
    }

    [TimedFact]
    public async Task HeartbeatAsync_WithGroups_ReturnsPauseStatePerGroup()
    {
        var serverId = await SeedServerAsync(pausedAt: null);
        var pausedGroupId = await SeedGroupAsync(serverId, pausedAt: DateTime.UtcNow);
        var activeGroupId = await SeedGroupAsync(serverId, pausedAt: null);

        var queries = TestTasks.QueriesFor(_fixture.CreateContext());
        await using var ctx = _fixture.CreateContext();
        var result = await queries.HeartbeatAsync(ctx, serverId, DateTime.UtcNow, null, null, default);

        result.ShouldNotBeNull();
        result.GroupPaused.Count.ShouldBe(2);
        result.GroupPaused[pausedGroupId].ShouldBeTrue();
        result.GroupPaused[activeGroupId].ShouldBeFalse();
    }

    [TimedFact]
    public async Task HeartbeatAsync_GroupsOnOtherServers_NotReturned()
    {
        var myServer = await SeedServerAsync(pausedAt: null);
        var otherServer = await SeedServerAsync(pausedAt: null);
        await SeedGroupAsync(otherServer, pausedAt: DateTime.UtcNow);

        var queries = TestTasks.QueriesFor(_fixture.CreateContext());
        await using var ctx = _fixture.CreateContext();
        var result = await queries.HeartbeatAsync(ctx, myServer, DateTime.UtcNow, null, null, default);

        result.ShouldNotBeNull();
        result.GroupPaused.ShouldBeEmpty();
    }

    private async Task<Guid> SeedServerAsync(DateTime? pausedAt, long? memoryBytes = null, double? cpuPercent = null)
    {
        await using var ctx = _fixture.CreateContext();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow.AddSeconds(-10);
        ctx.Set<Server>().Add(new Server
        {
            Id = id,
            StartedTime = now,
            LastHeartbeatTime = now,
            PausedAt = pausedAt,
            MemoryWorkingSetBytes = memoryBytes,
            CpuUsagePercent = cpuPercent,
        });
        await ctx.SaveChangesAsync();

        return id;
    }

    private async Task<Guid> SeedGroupAsync(Guid serverId, DateTime? pausedAt)
    {
        await using var ctx = _fixture.CreateContext();
        var group = new WorkerGroup
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            PausedAt = pausedAt,
            WorkerCount = 1,
        };
        ctx.Set<WorkerGroup>().Add(group);
        await ctx.SaveChangesAsync();

        return group.Id;
    }
}
