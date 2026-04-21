using Jobly.Core.Data.Entities;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Worker;

/// <summary>
/// End-to-end check that <see cref="Jobly.Worker.JoblyServerRegistration{TContext}.StopAsync"/>
/// actually removes Server + Worker rows on graceful shutdown. The shared-fixture integration
/// tests don't cover this because their server is disposed once at fixture teardown — and
/// because Respawn wipes the DB between tests, a silently broken StopAsync wouldn't fail them.
/// </summary>
[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class GracefulShutdownIntegrationTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected GracefulShutdownIntegrationTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task GivenRunningServer_WhenDisposed_ThenServerAndWorkerRowsRemoved()
    {
        // Arrange
        var server = await JoblyTestServer.StartAsync(_fixture);
        var serverId = server.ServerId;

        var ctx = _fixture.CreateContext();
        var serverBeforeDispose = await ctx.Set<Server>()
            .FirstOrDefaultAsync(x => x.Id == serverId, Xunit.TestContext.Current.CancellationToken);
        serverBeforeDispose.ShouldNotBeNull();

        var workersBeforeDispose = await ctx.Set<Jobly.Core.Data.Entities.Worker>()
            .Where(x => x.ServerId == serverId)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        workersBeforeDispose.Count.ShouldBeGreaterThan(0);

        // Act
        await server.DisposeAsync();

        // Assert
        var afterCtx = _fixture.CreateContext();
        var serverAfter = await afterCtx.Set<Server>()
            .FirstOrDefaultAsync(x => x.Id == serverId, Xunit.TestContext.Current.CancellationToken);
        serverAfter.ShouldBeNull();

        var workersAfter = await afterCtx.Set<Jobly.Core.Data.Entities.Worker>()
            .Where(x => x.ServerId == serverId)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        workersAfter.Count.ShouldBe(0);
    }
}
