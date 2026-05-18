using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.BackgroundServices;
using Warp.Core.Data.Entities;
using Warp.Tests.Fixtures;
using Warp.Tests.Helpers;

namespace Warp.Tests.BackgroundServices;

/// <summary>
/// Verifies that <c>ServerCleanup.CleanUpServersAsync</c> removes
/// <c>BackgroundServiceInstance</c> and <c>BackgroundServiceLease</c> rows that belong to a
/// dead server (i.e. one whose last heartbeat is older than <c>HealthCheckTimeout</c>).
/// This is the ungraceful-shutdown cleanup path — graceful shutdown is handled by
/// <c>WarpServerRegistration.StopAsync</c>.
/// </summary>
[GenerateDatabaseTests]
public abstract class UngracefulCleanupTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected UngracefulCleanupTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [TimedFact]
    public async Task ServerCleanup_DeadServerWithBackgroundServiceRows_RowsRemoved()
    {
        // Arrange — insert a dead server with a stale heartbeat.
        var deadServerId = Guid.NewGuid();
        var staleHeartbeat = DateTime.UtcNow.AddMinutes(-10);

        await using var ctx = _fixture.CreateContext();
        ctx.Set<Server>().Add(new Server
        {
            Id = deadServerId,
            StartedTime = staleHeartbeat,
            LastHeartbeatTime = staleHeartbeat,
        });

        // BackgroundServiceDefinition is required for FK integrity.
        ctx.Set<BackgroundServiceDefinition>().Add(new BackgroundServiceDefinition
        {
            Name = "UngracefulCleanupSvc",
            DeclaredScope = ServiceScope.Singleton,
            FirstSeenAt = staleHeartbeat,
            LastSeenAt = staleHeartbeat,
        });

        ctx.Set<BackgroundServiceInstance>().Add(new BackgroundServiceInstance
        {
            ServerId = deadServerId,
            ServiceName = "UngracefulCleanupSvc",
            Status = BackgroundServiceStatus.Running,
            StartedAt = staleHeartbeat,
            LastHeartbeatAt = staleHeartbeat,
        });

        ctx.Set<BackgroundServiceLease>().Add(new BackgroundServiceLease
        {
            ServiceName = "UngracefulCleanupSvc",
            HolderServerId = deadServerId,
            LeaseExpiresAt = DateTime.UtcNow.AddSeconds(-5),
        });

        await ctx.SaveChangesAsync(Ct);

        // Act — run cleanup with a 5-minute health-check timeout.
        // The server's last heartbeat was 10 minutes ago, so it qualifies as dead.
        var removed = await TestTasks
            .CreateServerCleanup(_fixture.CreateContext(), TimeProvider.System, TimeSpan.FromMinutes(5))
            .CleanUpServersAsync(Ct);

        // Assert — server removed and both child rows cleaned up.
        removed.ShouldBe(1);

        var readCtx = _fixture.CreateContext();

        var server = await readCtx.Set<Server>()
            .Where(x => x.Id == deadServerId)
            .FirstOrDefaultAsync(Ct);
        server.ShouldBeNull("dead server must be removed");

        var instance = await readCtx.Set<BackgroundServiceInstance>()
            .Where(x => x.ServerId == deadServerId)
            .FirstOrDefaultAsync(Ct);
        instance.ShouldBeNull("BackgroundServiceInstance must be removed for the dead server");

        var lease = await readCtx.Set<BackgroundServiceLease>()
            .Where(x => x.HolderServerId == deadServerId)
            .FirstOrDefaultAsync(Ct);
        lease.ShouldBeNull("BackgroundServiceLease must be removed for the dead server");
    }

    [TimedFact]
    public async Task ServerCleanup_LiveServerWithBackgroundServiceRows_RowsPreserved()
    {
        // Arrange — a live server with a fresh heartbeat must NOT be cleaned up.
        var liveServerId = Guid.NewGuid();

        await using var ctx = _fixture.CreateContext();
        ctx.Set<Server>().Add(new Server
        {
            Id = liveServerId,
            StartedTime = DateTime.UtcNow.AddMinutes(-1),
            LastHeartbeatTime = DateTime.UtcNow,
        });

        ctx.Set<BackgroundServiceDefinition>().Add(new BackgroundServiceDefinition
        {
            Name = "LiveCleanupSvc",
            DeclaredScope = ServiceScope.PerServer,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });

        ctx.Set<BackgroundServiceInstance>().Add(new BackgroundServiceInstance
        {
            ServerId = liveServerId,
            ServiceName = "LiveCleanupSvc",
            Status = BackgroundServiceStatus.Running,
            StartedAt = DateTime.UtcNow,
            LastHeartbeatAt = DateTime.UtcNow,
        });

        await ctx.SaveChangesAsync(Ct);

        // Act.
        var removed = await TestTasks
            .CreateServerCleanup(_fixture.CreateContext(), TimeProvider.System, TimeSpan.FromMinutes(5))
            .CleanUpServersAsync(Ct);

        // Assert.
        removed.ShouldBe(0, "live server must not be removed");

        var readCtx = _fixture.CreateContext();

        var instance = await readCtx.Set<BackgroundServiceInstance>()
            .Where(x => x.ServerId == liveServerId)
            .FirstOrDefaultAsync(Ct);
        instance.ShouldNotBeNull("instance row must be preserved for the live server");
    }
}
