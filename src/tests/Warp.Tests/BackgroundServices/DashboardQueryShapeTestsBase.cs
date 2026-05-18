using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;
using Warp.Core.BackgroundServices;
using Warp.Core.Data.Entities;
using Warp.Tests.Fixtures;

namespace Warp.Tests.BackgroundServices;

[GenerateDatabaseTests]
public abstract class DashboardQueryShapeTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected DashboardQueryShapeTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private BackgroundServiceQueryService<TestContext> CreateService()
    {
        return new BackgroundServiceQueryService<TestContext>(_fixture.CreateContext());
    }

    private async Task SeedDefinitionAsync(string name, ServiceScope scope)
    {
        var ctx = _fixture.CreateContext();
        ctx.Set<BackgroundServiceDefinition>().Add(new BackgroundServiceDefinition
        {
            Name = name,
            DeclaredScope = scope,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });

        await ctx.SaveChangesAsync(Ct);
    }

    private async Task SeedInstanceAsync(Guid serverId, string serviceName, ServiceScope scope, BackgroundServiceStatus status, int restartCount = 0, string? lastError = null, DateTime? lastErrorAt = null)
    {
        // Ensure a Server row exists for serverId (FK constraint). This is a convenience guard:
        // tests that want a named server call SeedServerAsync first; tests that don't care use
        // an auto-seeded row with a generated name.
        var serverCheckCtx = _fixture.CreateContext();
        var serverExists = await serverCheckCtx.Set<Server>()
            .AnyAsync(x => x.Id == serverId, Ct);

        if (!serverExists)
        {
            serverCheckCtx.Set<Server>().Add(new Server
            {
                Id = serverId,
                ServerName = $"auto-server-{serverId:N}",
                StartedTime = DateTime.UtcNow,
                LastHeartbeatTime = DateTime.UtcNow,
            });

            await serverCheckCtx.SaveChangesAsync(Ct);
        }

        var ctx = _fixture.CreateContext();
        ctx.Set<BackgroundServiceInstance>().Add(new BackgroundServiceInstance
        {
            ServerId = serverId,
            ServiceName = serviceName,
            DeclaredScope = scope,
            Status = status,
            StartedAt = DateTime.UtcNow,
            LastHeartbeatAt = DateTime.UtcNow,
            RestartCount = restartCount,
            LastError = lastError,
            LastErrorAt = lastErrorAt,
        });

        await ctx.SaveChangesAsync(Ct);
    }

    private async Task SeedLeaseAsync(string serviceName, Guid holderServerId)
    {
        var ctx = _fixture.CreateContext();
        ctx.Set<BackgroundServiceLease>().Add(new BackgroundServiceLease
        {
            ServiceName = serviceName,
            HolderServerId = holderServerId,
            LeaseExpiresAt = DateTime.UtcNow.AddSeconds(30),
        });

        await ctx.SaveChangesAsync(Ct);
    }

    [TimedFact]
    public async Task ListAsync_AggregatesPerServiceName()
    {
        var server1 = Guid.NewGuid();
        var server2 = Guid.NewGuid();
        var server3 = Guid.NewGuid();
        var server4 = Guid.NewGuid();

        await SeedDefinitionAsync("ServiceA", ServiceScope.PerServer);
        await SeedDefinitionAsync("ServiceB", ServiceScope.Singleton);

        await SeedInstanceAsync(server1, "ServiceA", ServiceScope.PerServer, BackgroundServiceStatus.Running);
        await SeedInstanceAsync(server2, "ServiceA", ServiceScope.PerServer, BackgroundServiceStatus.Faulted, restartCount: 3, lastError: "System.InvalidOperationException: bad state", lastErrorAt: DateTime.UtcNow);

        await SeedInstanceAsync(server3, "ServiceB", ServiceScope.Singleton, BackgroundServiceStatus.Running, restartCount: 1);
        await SeedInstanceAsync(server4, "ServiceB", ServiceScope.Singleton, BackgroundServiceStatus.Waiting);

        var svc = CreateService();
        var list = await svc.ListAsync(Ct);

        list.Count.ShouldBe(2);

        var serviceA = list.Single(x => string.Equals(x.Name, "ServiceA", StringComparison.Ordinal));
        serviceA.Scope.ShouldBe(ServiceScope.PerServer);
        serviceA.TotalInstances.ShouldBe(2);
        serviceA.RunningCount.ShouldBe(1);
        serviceA.FaultedCount.ShouldBe(1);
        serviceA.WaitingCount.ShouldBe(0);
        serviceA.TotalRestartCount.ShouldBe(3);
        serviceA.LastErrorType.ShouldBe("System.InvalidOperationException");

        var serviceB = list.Single(x => string.Equals(x.Name, "ServiceB", StringComparison.Ordinal));
        serviceB.Scope.ShouldBe(ServiceScope.Singleton);
        serviceB.TotalInstances.ShouldBe(2);
        serviceB.RunningCount.ShouldBe(1);
        serviceB.WaitingCount.ShouldBe(1);
        serviceB.FaultedCount.ShouldBe(0);
        serviceB.TotalRestartCount.ShouldBe(1);
        serviceB.LastErrorType.ShouldBeNull();
    }

    [TimedFact]
    public async Task ListAsync_SurfacesConfigurationMismatch()
    {
        var serverId = Guid.NewGuid();

        await SeedDefinitionAsync("MismatchSvc", ServiceScope.PerServer);
        await SeedInstanceAsync(serverId, "MismatchSvc", ServiceScope.Singleton, BackgroundServiceStatus.ConfigurationMismatch);

        var svc = CreateService();
        var list = await svc.ListAsync(Ct);

        list.Count.ShouldBe(1);
        list[0].ConfigurationMismatchCount.ShouldBe(1);
        list[0].TotalInstances.ShouldBe(1);
    }

    [TimedFact]
    public async Task GetAsync_UnknownName_ReturnsNull()
    {
        var svc = CreateService();
        var result = await svc.GetAsync("DoesNotExist", Ct);

        result.ShouldBeNull();
    }

    [TimedFact]
    public async Task GetAsync_KnownName_ReturnsAllInstanceRows()
    {
        var server1 = Guid.NewGuid();
        var server2 = Guid.NewGuid();

        await _fixture.SeedServerAsync(server1, "detail-server-1");
        await _fixture.SeedServerAsync(server2, "detail-server-2");
        await SeedDefinitionAsync("DetailSvc", ServiceScope.PerServer);
        await SeedInstanceAsync(server1, "DetailSvc", ServiceScope.PerServer, BackgroundServiceStatus.Running);
        await SeedInstanceAsync(server2, "DetailSvc", ServiceScope.PerServer, BackgroundServiceStatus.Faulted, restartCount: 2, lastError: "System.Exception: oops", lastErrorAt: DateTime.UtcNow);

        var svc = CreateService();
        var detail = await svc.GetAsync("DetailSvc", Ct);

        detail.ShouldNotBeNull();
        detail!.Name.ShouldBe("DetailSvc");
        detail.DeclaredScope.ShouldBe(ServiceScope.PerServer);
        detail.Instances.Count.ShouldBe(2);

        var running = detail.Instances.Single(i => i.Status == BackgroundServiceStatus.Running);
        running.ServerId.ShouldBe(server1);
        running.ServerName.ShouldBe("detail-server-1");

        var faulted = detail.Instances.Single(i => i.Status == BackgroundServiceStatus.Faulted);
        faulted.ServerId.ShouldBe(server2);
        faulted.ServerName.ShouldBe("detail-server-2");
        faulted.RestartCount.ShouldBe(2);
        faulted.LastError.ShouldBe("System.Exception: oops");
    }

    [TimedFact]
    public async Task GetLeaseAsync_PerServerService_ReturnsNull()
    {
        var serverId = Guid.NewGuid();

        await SeedDefinitionAsync("PerServerSvc", ServiceScope.PerServer);
        await SeedInstanceAsync(serverId, "PerServerSvc", ServiceScope.PerServer, BackgroundServiceStatus.Running);

        // No lease row inserted — per-server services never acquire a lease.
        var svc = CreateService();
        var lease = await svc.GetLeaseAsync("PerServerSvc", Ct);

        lease.ShouldBeNull();
    }

    [TimedFact]
    public async Task GetLeaseAsync_SingletonWithLease_ReturnsLeaseDto()
    {
        var holderServerId = Guid.NewGuid();

        await _fixture.SeedServerAsync(holderServerId, "singleton-holder");
        await SeedDefinitionAsync("SingletonSvc", ServiceScope.Singleton);
        await SeedInstanceAsync(holderServerId, "SingletonSvc", ServiceScope.Singleton, BackgroundServiceStatus.Running);
        await SeedLeaseAsync("SingletonSvc", holderServerId);

        var svc = CreateService();
        var lease = await svc.GetLeaseAsync("SingletonSvc", Ct);

        lease.ShouldNotBeNull();
        lease!.ServiceName.ShouldBe("SingletonSvc");
        lease.HolderServerId.ShouldBe(holderServerId);
        lease.HolderServerName.ShouldBe("singleton-holder");
        lease.LeaseExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [TimedFact]
    public async Task GetLogsAsync_FilterBySource_Lifecycle_OnlyLifecycleRows()
    {
        var serverId = Guid.NewGuid();

        await _fixture.SeedServerAsync(serverId, "log-server-1");
        await SeedDefinitionAsync("LogSvc", ServiceScope.PerServer);
        await SeedInstanceAsync(serverId, "LogSvc", ServiceScope.PerServer, BackgroundServiceStatus.Running);

        var ctx = _fixture.CreateContext();
        ctx.Set<BackgroundServiceLog>().Add(new BackgroundServiceLog
        {
            ServerId = serverId,
            ServiceName = "LogSvc",
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Information,
            Source = BackgroundServiceLogSource.Lifecycle,
            Message = "Service started",
        });
        ctx.Set<BackgroundServiceLog>().Add(new BackgroundServiceLog
        {
            ServerId = serverId,
            ServiceName = "LogSvc",
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Information,
            Source = BackgroundServiceLogSource.User,
            Message = "User log message",
        });
        await ctx.SaveChangesAsync(Ct);

        var svc = CreateService();
        var logs = await svc.GetLogsAsync("LogSvc", BackgroundServiceLogSource.Lifecycle, null, null, 100, Ct);

        logs.Count.ShouldBe(1);
        logs[0].Source.ShouldBe(BackgroundServiceLogSource.Lifecycle);
        logs[0].Message.ShouldBe("Service started");
        logs[0].ServerName.ShouldBe("log-server-1");
    }

    [TimedFact]
    public async Task GetLogsAsync_FilterByMinLevel_DropsBelowThreshold()
    {
        var serverId = Guid.NewGuid();

        await SeedDefinitionAsync("LevelSvc", ServiceScope.PerServer);
        await SeedInstanceAsync(serverId, "LevelSvc", ServiceScope.PerServer, BackgroundServiceStatus.Running);

        var ctx = _fixture.CreateContext();
        ctx.Set<BackgroundServiceLog>().Add(new BackgroundServiceLog
        {
            ServerId = serverId,
            ServiceName = "LevelSvc",
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Debug,
            Source = BackgroundServiceLogSource.User,
            Message = "Debug message",
        });
        ctx.Set<BackgroundServiceLog>().Add(new BackgroundServiceLog
        {
            ServerId = serverId,
            ServiceName = "LevelSvc",
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Warning,
            Source = BackgroundServiceLogSource.User,
            Message = "Warning message",
        });
        await ctx.SaveChangesAsync(Ct);

        var svc = CreateService();
        var logs = await svc.GetLogsAsync("LevelSvc", null, LogLevel.Information, null, 100, Ct);

        logs.Count.ShouldBe(1);
        logs[0].Level.ShouldBe(LogLevel.Warning);
    }

    [TimedFact]
    public async Task GetLogsAsync_PaginateByFromId_ReturnsExpectedSlice()
    {
        var serverId = Guid.NewGuid();

        await SeedDefinitionAsync("PagingSvc", ServiceScope.PerServer);
        await SeedInstanceAsync(serverId, "PagingSvc", ServiceScope.PerServer, BackgroundServiceStatus.Running);

        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 5; i++)
        {
            ctx.Set<BackgroundServiceLog>().Add(new BackgroundServiceLog
            {
                ServerId = serverId,
                ServiceName = "PagingSvc",
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Information,
                Source = BackgroundServiceLogSource.User,
                Message = $"Message {i}",
            });
        }

        await ctx.SaveChangesAsync(Ct);

        // Fetch all 5 to find the median Id for paging.
        var svc = CreateService();
        var allLogs = await svc.GetLogsAsync("PagingSvc", null, null, null, 100, Ct);
        allLogs.Count.ShouldBe(5);

        // The query returns newest-first. The 3rd item (index 2) is the "pivot" fromId.
        // fromId means "last seen id; give me rows NEWER than this" → returns rows with Id > pivotId.
        var pivotId = allLogs[2].Id;
        var newerPage = await svc.GetLogsAsync("PagingSvc", null, null, pivotId, 100, Ct);

        // Should return only rows with Id > pivotId (the 2 newest rows), ordered newest-first.
        newerPage.Count.ShouldBe(2);
        newerPage.ShouldAllBe(l => l.Id > pivotId);
    }

    [TimedFact]
    public async Task GetLogsAsync_WithFromId_ReturnsRowsWithGreaterIdsNewestFirst()
    {
        var serverId = Guid.NewGuid();

        await SeedDefinitionAsync("CursorSvc", ServiceScope.PerServer);
        await SeedInstanceAsync(serverId, "CursorSvc", ServiceScope.PerServer, BackgroundServiceStatus.Running);

        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 5; i++)
        {
            ctx.Set<BackgroundServiceLog>().Add(new BackgroundServiceLog
            {
                ServerId = serverId,
                ServiceName = "CursorSvc",
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Information,
                Source = BackgroundServiceLogSource.User,
                Message = $"Message {i}",
            });
        }

        await ctx.SaveChangesAsync(Ct);

        var svc = CreateService();

        // Fetch all 5 rows (newest-first order — highest Id first).
        var allLogs = await svc.GetLogsAsync("CursorSvc", null, null, null, 100, Ct);
        allLogs.Count.ShouldBe(5);

        // Use the 3rd row (index 2, middle of the list) as the cursor.
        var cursorId = allLogs[2].Id;

        // fromId = "last seen id; give me rows newer than this" → must return only Id > cursorId.
        var result = await svc.GetLogsAsync("CursorSvc", null, null, cursorId, 10, Ct);

        result.Count.ShouldBe(2);
        result.ShouldAllBe(l => l.Id > cursorId);

        // Verify newest-first order within the returned slice.
        result[0].Id.ShouldBeGreaterThan(result[1].Id);
    }

    [TimedFact]
    public async Task ListAsync_OrdersByFirstSeenAt_OldestFirst()
    {
        // Insert three definitions in non-chronological order: Middle, then Oldest, then Newest.
        // The query must return them ordered by FirstSeenAt ascending regardless of insertion order.
        var ctx = _fixture.CreateContext();
        var now = DateTime.UtcNow;

        ctx.Set<BackgroundServiceDefinition>().Add(new BackgroundServiceDefinition
        {
            Name = "MiddleSvc",
            DeclaredScope = ServiceScope.PerServer,
            FirstSeenAt = now.AddHours(-1),
            LastSeenAt = now,
        });

        await ctx.SaveChangesAsync(Ct);

        var ctx2 = _fixture.CreateContext();
        ctx2.Set<BackgroundServiceDefinition>().Add(new BackgroundServiceDefinition
        {
            Name = "OldestSvc",
            DeclaredScope = ServiceScope.PerServer,
            FirstSeenAt = now.AddHours(-3),
            LastSeenAt = now,
        });

        await ctx2.SaveChangesAsync(Ct);

        var ctx3 = _fixture.CreateContext();
        ctx3.Set<BackgroundServiceDefinition>().Add(new BackgroundServiceDefinition
        {
            Name = "NewestSvc",
            DeclaredScope = ServiceScope.PerServer,
            FirstSeenAt = now.AddMinutes(-30),
            LastSeenAt = now,
        });

        await ctx3.SaveChangesAsync(Ct);

        var svc = CreateService();
        var list = await svc.ListAsync(Ct);

        list.Count.ShouldBe(3);
        list[0].Name.ShouldBe("OldestSvc", "First result should be the service registered 3h ago");
        list[1].Name.ShouldBe("MiddleSvc", "Second result should be the service registered 1h ago");
        list[2].Name.ShouldBe("NewestSvc", "Third result should be the service registered 30min ago");
    }

    [TimedFact]
    public async Task GetAsync_InstancesOrderedByStartedAt_OldestFirst()
    {
        // Insert two instances for the same service — newer first, to prove the ORDER BY clause.
        var server1 = Guid.NewGuid();
        var server2 = Guid.NewGuid();

        await SeedDefinitionAsync("OrderedSvc", ServiceScope.PerServer);

        // Insert newer instance first (SeedInstanceAsync auto-seeds the Server row).
        await SeedInstanceAsync(server1, "OrderedSvc", ServiceScope.PerServer, BackgroundServiceStatus.Running);
        await SeedInstanceAsync(server2, "OrderedSvc", ServiceScope.PerServer, BackgroundServiceStatus.Running);

        // Adjust StartedAt to produce a deterministic ordering: server2 older, server1 newer.
        // Update directly via a new context so the ORDER BY assertion is meaningful.
        var updateCtx = _fixture.CreateContext();
        var now = DateTime.UtcNow;

        await updateCtx.Set<BackgroundServiceInstance>()
            .Where(x => x.ServerId == server1 && x.ServiceName == "OrderedSvc")
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.StartedAt, now.AddMinutes(-10)), Ct);

        await updateCtx.Set<BackgroundServiceInstance>()
            .Where(x => x.ServerId == server2 && x.ServiceName == "OrderedSvc")
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.StartedAt, now.AddHours(-1)), Ct);

        var svc = CreateService();
        var detail = await svc.GetAsync("OrderedSvc", Ct);

        detail.ShouldNotBeNull();
        detail!.Instances.Count.ShouldBe(2);
        detail.Instances[0].StartedAt.ShouldBeLessThan(
            detail.Instances[1].StartedAt,
            "Instances must be ordered by StartedAt ascending (oldest first)");
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;
}
