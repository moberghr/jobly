using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Services;
using Warp.Tests.Fixtures;

namespace Warp.Tests.Admin;

[GenerateDatabaseTests]
public abstract class ServerCommandServiceTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected ServerCommandServiceTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task PauseServer_SetsPausedAt()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = new ServerCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.PauseServer(serverId);

        // Assert
        result.ShouldBeTrue();
        var server = await _fixture.CreateContext().Set<Server>().FindAsync([serverId], Xunit.TestContext.Current.CancellationToken);
        server.ShouldNotBeNull();
        server.PausedAt.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task ResumeServer_ClearsPausedAt()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            PausedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = new ServerCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.ResumeServer(serverId);

        // Assert
        result.ShouldBeTrue();
        var server = await _fixture.CreateContext().Set<Server>().FindAsync([serverId], Xunit.TestContext.Current.CancellationToken);
        server.ShouldNotBeNull();
        server.PausedAt.ShouldBeNull();
    }

    [TimedFact]
    public async Task PauseServer_ReturnsFalseForNonexistent()
    {
        var svc = new ServerCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.PauseServer(Guid.NewGuid());
        result.ShouldBeFalse();
    }

    [TimedFact]
    public async Task PauseWorkerGroup_SetsPausedAt()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });

        var groupId = Guid.NewGuid();
        ctx.Set<WorkerGroup>().Add(new WorkerGroup
        {
            Id = groupId,
            ServerId = serverId,
            WorkerCount = 1,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = new ServerCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.PauseWorkerGroup(groupId);

        // Assert
        result.ShouldBeTrue();
        var group = await _fixture.CreateContext().Set<WorkerGroup>().FindAsync([groupId], Xunit.TestContext.Current.CancellationToken);
        group.ShouldNotBeNull();
        group.PausedAt.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task ResumeWorkerGroup_ClearsPausedAt()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });

        var groupId = Guid.NewGuid();
        ctx.Set<WorkerGroup>().Add(new WorkerGroup
        {
            Id = groupId,
            ServerId = serverId,
            WorkerCount = 1,
            PausedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = new ServerCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.ResumeWorkerGroup(groupId);

        // Assert
        result.ShouldBeTrue();
        var group = await _fixture.CreateContext().Set<WorkerGroup>().FindAsync([groupId], Xunit.TestContext.Current.CancellationToken);
        group.ShouldNotBeNull();
        group.PausedAt.ShouldBeNull();
    }

    [TimedFact]
    public async Task PauseWorkerGroup_ReturnsFalseForNonexistent()
    {
        var svc = new ServerCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.PauseWorkerGroup(Guid.NewGuid());
        result.ShouldBeFalse();
    }

    [TimedFact]
    public async Task ResumeServer_ReturnsFalseForNonexistent()
    {
        var svc = new ServerCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.ResumeServer(Guid.NewGuid());
        result.ShouldBeFalse();
    }

    [TimedFact]
    public async Task ResumeWorkerGroup_ReturnsFalseForNonexistent()
    {
        var svc = new ServerCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.ResumeWorkerGroup(Guid.NewGuid());
        result.ShouldBeFalse();
    }

    [TimedFact]
    public async Task PauseServer_AlreadyPaused_UpdatesTimestamp()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        var originalPausedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            PausedAt = originalPausedAt,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act — pause again
        var svc = new ServerCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.PauseServer(serverId);

        // Assert — timestamp should be updated (idempotent but refreshes time)
        result.ShouldBeTrue();
        var server = await _fixture.CreateContext().Set<Server>().FindAsync([serverId], Xunit.TestContext.Current.CancellationToken);
        server.ShouldNotBeNull();
        server.PausedAt.ShouldNotBeNull();
        server.PausedAt.ShouldNotBe(originalPausedAt);
    }
}
