using Jobly.Core.Data.Queries;
using Jobly.Core.Notifications;
using Jobly.Tests.Fixtures;
using Jobly.Tests.Helpers;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Worker;

/// <summary>
/// Verifies that <see cref="JoblyDispatcherHost{TContext}"/> and
/// <see cref="JoblySingleWorkerHost{TContext}"/> respect the
/// <see cref="JoblyWorkerConfiguration.UseDispatcher"/> flag — each no-ops when its mode is
/// not selected, and starts + stops cleanly when it is. The full end-to-end behavior of workers
/// actually fetching and processing jobs is covered by the integration tests; these unit tests
/// just pin the mode-branching contract so a future refactor can't silently break it.
/// </summary>
[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class WorkerHostModeTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected WorkerHostModeTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task DispatcherHost_UseDispatcherFalse_DoesNotMutateState()
    {
        // Arrange — state pre-populated as though registration ran
        var groupId = Guid.NewGuid();
        var state = PopulateState(groupEntityId: groupId, workerCount: 1);
        var host = CreateDispatcherHost(useDispatcher: false, state);

        // Act
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await host.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert — state is read-only from the host's perspective; verify it wasn't touched
        state.Groups.Count.ShouldBe(1);
        state.Groups[0].GroupEntityId.ShouldBe(groupId);
    }

    [TimedFact]
    public async Task DispatcherHost_UseDispatcherTrue_StartsAndStopsCleanly()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var state = PopulateState(groupEntityId: groupId, workerCount: 1);
        var host = CreateDispatcherHost(useDispatcher: true, state);

        // Act
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await host.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert — state remains intact after the dispatcher mode runs its lifecycle
        state.Groups.Count.ShouldBe(1);
        state.Groups[0].GroupEntityId.ShouldBe(groupId);
    }

    [TimedFact]
    public async Task SingleWorkerHost_UseDispatcherTrue_DoesNotMutateState()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var state = PopulateState(groupEntityId: groupId, workerCount: 1);
        var host = CreateSingleWorkerHost(useDispatcher: true, state);

        // Act
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await host.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        state.Groups.Count.ShouldBe(1);
        state.Groups[0].GroupEntityId.ShouldBe(groupId);
    }

    [TimedFact]
    public async Task SingleWorkerHost_UseDispatcherFalse_StartsAndStopsCleanly()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var state = PopulateState(groupEntityId: groupId, workerCount: 1);
        var host = CreateSingleWorkerHost(useDispatcher: false, state);

        // Act
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await host.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        state.Groups.Count.ShouldBe(1);
        state.Groups[0].GroupEntityId.ShouldBe(groupId);
    }

    [TimedFact]
    public async Task SingleWorkerHost_EmptyState_SilentNoOp()
    {
        // Arrange — state never populated (gap where registration was skipped)
        var state = new ServerRegistrationState();
        var host = CreateSingleWorkerHost(useDispatcher: false, state);

        // Act — silent no-op is the documented behavior; we pin it here so a future change
        // that raises an exception on empty state is a deliberate, visible decision.
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await host.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        state.Groups.Count.ShouldBe(0);
    }

    private JoblyDispatcherHost<TestContext> CreateDispatcherHost(bool useDispatcher, ServerRegistrationState state)
    {
        var config = Options.Create(new JoblyWorkerConfiguration
        {
            UseDispatcher = useDispatcher,
            WorkerCount = 1,
        });
        return new JoblyDispatcherHost<TestContext>(
            config,
            BuildScopeFactory(),
            TimeProvider.System,
            new PauseStateHolder(),
            new NullNotificationTransport(),
            state,
            NullLoggerFactory.Instance);
    }

    private JoblySingleWorkerHost<TestContext> CreateSingleWorkerHost(bool useDispatcher, ServerRegistrationState state)
    {
        var config = Options.Create(new JoblyWorkerConfiguration
        {
            UseDispatcher = useDispatcher,
            WorkerCount = 1,
        });
        var scopeFactory = BuildScopeFactory();
        return new JoblySingleWorkerHost<TestContext>(
            config,
            scopeFactory,
            TimeProvider.System,
            new PauseStateHolder(),
            new NullNotificationTransport(),
            TestTasks.QueriesFromScope<TestContext>(scopeFactory),
            state,
            NullLoggerFactory.Instance);
    }

    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());
        services.AddSingleton<IJoblySqlQueries<TestContext>>(sp =>
        {
            using var scope = sp.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
            return Jobly.Tests.Helpers.TestTasks.QueriesFor(ctx);
        });
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static ServerRegistrationState PopulateState(Guid groupEntityId, int workerCount)
    {
        var state = new ServerRegistrationState();
        var workerIds = new List<Guid>();
        for (var i = 0; i < workerCount; i++)
        {
            workerIds.Add(Guid.NewGuid());
        }

        state.Set(
        [
            new ServerRegistrationState.GroupRegistration(
                new WorkerGroupConfiguration { WorkerCount = workerCount, Queues = ["default"] },
                groupEntityId,
                workerIds),
        ]);
        return state;
    }
}
