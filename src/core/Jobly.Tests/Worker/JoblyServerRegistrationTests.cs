using Jobly.Core.Data.Entities;
using Jobly.Tests.Fixtures;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Worker;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class JoblyServerRegistrationTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected JoblyServerRegistrationTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task StartAsync_WritesServerRow()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var (registration, _) = CreateRegistration(serverId);

        // Act
        await registration.StartAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>().FirstOrDefaultAsync(x => x.Id == serverId, Xunit.TestContext.Current.CancellationToken);
        server.ShouldNotBeNull();
        server.ServiceCount.ShouldBe(2);
    }

    [TimedFact]
    public async Task StartAsync_WritesWorkerGroupAndWorkerRows()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var (registration, _) = CreateRegistration(serverId);

        // Act
        await registration.StartAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        var readCtx = _fixture.CreateContext();
        var groups = await readCtx.Set<Jobly.Core.Data.Entities.WorkerGroup>()
            .Where(x => x.ServerId == serverId)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        groups.Count.ShouldBe(1);
        groups[0].WorkerCount.ShouldBe(2);

        var workers = await readCtx.Set<Jobly.Core.Data.Entities.Worker>()
            .Where(x => x.ServerId == serverId)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        workers.Count.ShouldBe(2);
    }

    [TimedFact]
    public async Task StartAsync_PopulatesRegistrationState()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var (registration, state) = CreateRegistration(serverId);

        // Act
        await registration.StartAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        state.Groups.Count.ShouldBe(1);
        state.Groups[0].WorkerIds.Count.ShouldBe(2);
        state.Groups[0].GroupEntityId.ShouldNotBe(Guid.Empty);
    }

    [TimedFact]
    public async Task StopAsync_RemovesServerAndWorkerRows()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var (registration, _) = CreateRegistration(serverId);
        await registration.StartAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        await registration.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>().FirstOrDefaultAsync(x => x.Id == serverId, Xunit.TestContext.Current.CancellationToken);
        server.ShouldBeNull();

        var workers = await readCtx.Set<Jobly.Core.Data.Entities.Worker>()
            .Where(x => x.ServerId == serverId)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        workers.Count.ShouldBe(0);
    }

    [TimedFact]
    public async Task StopAsync_NoServerRow_GracefulNoOp()
    {
        // Arrange — registration that never ran StartAsync
        var serverId = Guid.NewGuid();
        var (registration, _) = CreateRegistration(serverId);

        // Act
        await registration.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert — no server row was created as a side effect, no exception thrown
        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>().FirstOrDefaultAsync(x => x.Id == serverId, Xunit.TestContext.Current.CancellationToken);
        server.ShouldBeNull();
    }

    private (JoblyServerRegistration<TestContext> Registration, ServerRegistrationState State) CreateRegistration(Guid serverId)
    {
        var config = Options.Create(new JoblyWorkerConfiguration
        {
            ServerId = serverId,
            WorkerCount = 2,
        });
        var services = new ServiceCollection();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var state = new ServerRegistrationState();
        var pauseStateHolder = new PauseStateHolder();
        var registration = new JoblyServerRegistration<TestContext>(
            config,
            scopeFactory,
            TimeProvider.System,
            pauseStateHolder,
            state);
        return (registration, state);
    }
}
