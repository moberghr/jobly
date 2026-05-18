using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.BackgroundServices;
using Warp.Core.Data.Entities;
using Warp.Tests.Fixtures;

namespace Warp.Tests.BackgroundServices;

[GenerateDatabaseTests]
public abstract class EntitySchemaTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected EntitySchemaTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task Insert_DefinitionThenInstance_Succeeds()
    {
        var ctx = _fixture.CreateContext();
        ctx.Set<BackgroundServiceDefinition>().Add(new BackgroundServiceDefinition
        {
            Name = "TestService",
            DeclaredScope = ServiceScope.PerServer,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(TestCancellation);

        var serverId = Guid.NewGuid();
        await _fixture.SeedServerAsync(serverId, "test-server-1");
        var instanceCtx = _fixture.CreateContext();
        instanceCtx.Set<BackgroundServiceInstance>().Add(new BackgroundServiceInstance
        {
            ServerId = serverId,
            ServiceName = "TestService",
            DeclaredScope = ServiceScope.PerServer,
            Status = BackgroundServiceStatus.Running,
            StartedAt = DateTime.UtcNow,
            LastHeartbeatAt = DateTime.UtcNow,
            RestartCount = 0,
        });
        await instanceCtx.SaveChangesAsync(TestCancellation);

        var readCtx = _fixture.CreateContext();
        var def = await readCtx.Set<BackgroundServiceDefinition>().FindAsync(["TestService"], TestCancellation);
        var inst = await readCtx.Set<BackgroundServiceInstance>().FindAsync([serverId, "TestService"], TestCancellation);

        def.ShouldNotBeNull();
        def.DeclaredScope.ShouldBe(ServiceScope.PerServer);
        inst.ShouldNotBeNull();
        inst.Status.ShouldBe(BackgroundServiceStatus.Running);
    }

    [TimedFact]
    public async Task Insert_InstanceWithoutDefinition_ThrowsRestrict()
    {
        var ctx = _fixture.CreateContext();
        ctx.Set<BackgroundServiceInstance>().Add(new BackgroundServiceInstance
        {
            ServerId = Guid.NewGuid(),
            ServiceName = "OrphanService",
            DeclaredScope = ServiceScope.PerServer,
            Status = BackgroundServiceStatus.Running,
            StartedAt = DateTime.UtcNow,
            LastHeartbeatAt = DateTime.UtcNow,
            RestartCount = 0,
        });

        await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(TestCancellation));
    }

    [TimedFact]
    public async Task Insert_LeaseWithoutDefinition_ThrowsRestrict()
    {
        var ctx = _fixture.CreateContext();
        ctx.Set<BackgroundServiceLease>().Add(new BackgroundServiceLease
        {
            ServiceName = "OrphanService",
            HolderServerId = Guid.NewGuid(),
            LeaseExpiresAt = DateTime.UtcNow.AddSeconds(30),
        });

        await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(TestCancellation));
    }

    [TimedFact]
    public async Task Delete_Instance_CascadesLogRows()
    {
        var serverId = Guid.NewGuid();
        await _fixture.SeedServerAsync(serverId, "test-server-2");
        var arrangeCtx = _fixture.CreateContext();
        arrangeCtx.Set<BackgroundServiceDefinition>().Add(new BackgroundServiceDefinition
        {
            Name = "CascadeService",
            DeclaredScope = ServiceScope.PerServer,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        arrangeCtx.Set<BackgroundServiceInstance>().Add(new BackgroundServiceInstance
        {
            ServerId = serverId,
            ServiceName = "CascadeService",
            DeclaredScope = ServiceScope.PerServer,
            Status = BackgroundServiceStatus.Running,
            StartedAt = DateTime.UtcNow,
            LastHeartbeatAt = DateTime.UtcNow,
            RestartCount = 0,
        });
        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var logCtx = _fixture.CreateContext();
        for (var i = 0; i < 5; i++)
        {
            logCtx.Set<BackgroundServiceLog>().Add(new BackgroundServiceLog
            {
                ServerId = serverId,
                ServiceName = "CascadeService",
                Timestamp = DateTime.UtcNow,
                Level = Microsoft.Extensions.Logging.LogLevel.Information,
                Source = BackgroundServiceLogSource.Lifecycle,
                Message = $"Log entry {i}",
            });
        }

        await logCtx.SaveChangesAsync(TestCancellation);

        var deleteCtx = _fixture.CreateContext();
        var instance = await deleteCtx.Set<BackgroundServiceInstance>()
            .Where(x => x.ServerId == serverId)
            .Where(x => x.ServiceName == "CascadeService")
            .FirstAsync(TestCancellation);
        deleteCtx.Set<BackgroundServiceInstance>().Remove(instance);
        await deleteCtx.SaveChangesAsync(TestCancellation);

        var readCtx = _fixture.CreateContext();
        var logCount = await readCtx.Set<BackgroundServiceLog>()
            .Where(x => x.ServerId == serverId)
            .Where(x => x.ServiceName == "CascadeService")
            .CountAsync(TestCancellation);

        logCount.ShouldBe(0);
    }

    [TimedFact]
    public async Task Delete_Definition_ThrowsWhenInstancesExist()
    {
        var serverId = Guid.NewGuid();
        await _fixture.SeedServerAsync(serverId, "test-server-3");
        var arrangeCtx = _fixture.CreateContext();
        arrangeCtx.Set<BackgroundServiceDefinition>().Add(new BackgroundServiceDefinition
        {
            Name = "RestrictService",
            DeclaredScope = ServiceScope.PerServer,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        arrangeCtx.Set<BackgroundServiceInstance>().Add(new BackgroundServiceInstance
        {
            ServerId = serverId,
            ServiceName = "RestrictService",
            DeclaredScope = ServiceScope.PerServer,
            Status = BackgroundServiceStatus.Running,
            StartedAt = DateTime.UtcNow,
            LastHeartbeatAt = DateTime.UtcNow,
            RestartCount = 0,
        });
        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var deleteCtx = _fixture.CreateContext();
        var def = await deleteCtx.Set<BackgroundServiceDefinition>().FindAsync(["RestrictService"], TestCancellation);
        deleteCtx.Set<BackgroundServiceDefinition>().Remove(def!);

        await Should.ThrowAsync<DbUpdateException>(() => deleteCtx.SaveChangesAsync(TestCancellation));
    }

    [TimedFact]
    public void BackgroundServiceLog_HasServiceNameIdDescIndex()
    {
        var ctx = _fixture.CreateContext();
        var model = ctx.Model;
        var logEntity = model.FindEntityType(typeof(BackgroundServiceLog))!;
        var indexes = logEntity.GetIndexes().ToList();

        // Must have the (ServiceName, Id) index for the cross-server log-tail dashboard query.
        // The IsDescending(false, true) configuration (ServiceName ASC, Id DESC) lives in
        // ServiceConfiguration.cs; EF Core's runtime model doesn't surface it on IReadOnlyIndex
        // without the design-time model, so we assert structural presence only here.
        var serviceNameIdIndex = indexes.FirstOrDefault(ix =>
        {
            var props = ix.Properties;
            return props.Count == 2
                && string.Equals(props[0].Name, nameof(BackgroundServiceLog.ServiceName), StringComparison.Ordinal)
                && string.Equals(props[1].Name, nameof(BackgroundServiceLog.Id), StringComparison.Ordinal);
        });

        serviceNameIdIndex.ShouldNotBeNull("BackgroundServiceLog must have a (ServiceName, Id) index for the cross-server log-tail query");
    }

    [TimedFact]
    public void Schema_EntitiesUsesWarpSchema()
    {
        var ctx = _fixture.CreateContext();
        var model = ctx.Model;

        model.FindEntityType(typeof(BackgroundServiceDefinition))!.GetSchema().ShouldBe("warp");
        model.FindEntityType(typeof(BackgroundServiceInstance))!.GetSchema().ShouldBe("warp");
        model.FindEntityType(typeof(BackgroundServiceLease))!.GetSchema().ShouldBe("warp");
        model.FindEntityType(typeof(BackgroundServiceLog))!.GetSchema().ShouldBe("warp");
    }

    [TimedFact]
    public void Schema_SnakeCaseConvention_AppliesWhenEnabled()
    {
        // This test verifies that when UseSnakeCaseNamingConvention() is applied (Postgres),
        // the multi-word column names are lowercased with underscores. SQL Server doesn't use
        // that convention, so we detect which naming is active via the existing LastHeartbeatAt
        // column — if it's snake_case we assert the full set.
        var ctx = _fixture.CreateContext();
        var model = ctx.Model;
        var instEntity = model.FindEntityType(typeof(BackgroundServiceInstance))!;
        var heartbeatColName = instEntity.FindProperty(nameof(BackgroundServiceInstance.LastHeartbeatAt))!.GetColumnName();

        if (string.Equals(heartbeatColName, "last_heartbeat_at", StringComparison.Ordinal))
        {
            // Snake-case fixture (Postgres). Assert multi-word columns use snake_case.
            instEntity.FindProperty(nameof(BackgroundServiceInstance.ServiceName))!
                .GetColumnName().ShouldBe("service_name");

            var logEntity = model.FindEntityType(typeof(BackgroundServiceLog))!;
            logEntity.FindProperty(nameof(BackgroundServiceLog.ExceptionType))!
                .GetColumnName().ShouldBe("exception_type");
            logEntity.FindProperty(nameof(BackgroundServiceLog.ExceptionMessage))!
                .GetColumnName().ShouldBe("exception_message");

            var defEntity = model.FindEntityType(typeof(BackgroundServiceDefinition))!;
            defEntity.FindProperty(nameof(BackgroundServiceDefinition.DeclaredScope))!
                .GetColumnName().ShouldBe("declared_scope");
        }
        else
        {
            // PascalCase fixture (SQL Server). Assert multi-word columns use PascalCase.
            instEntity.FindProperty(nameof(BackgroundServiceInstance.ServiceName))!
                .GetColumnName().ShouldBe("ServiceName");
        }
    }

    private static CancellationToken TestCancellation => Xunit.TestContext.Current.CancellationToken;
}
