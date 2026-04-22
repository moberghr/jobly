using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Jobly.Core.Entities;
using Jobly.Core.Notifications;
using Jobly.Tests.Fixtures;
using Jobly.Tests.Helpers;
using Jobly.Worker;
using Jobly.Worker.Services;
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
    public async Task DispatcherHost_UseDispatcherFalse_IsSilentNoOp()
    {
        // Arrange — state pre-populated as though registration ran
        var state = PopulateState(groupEntityId: Guid.NewGuid(), workerCount: 1);
        var host = CreateDispatcherHost(useDispatcher: false, state);

        // Act
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await host.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert — the wrong-mode host must not write any side-effect rows. JoblyServerRegistration
        // owns Server/Worker/WorkerGroup insertion; a host that accidentally duplicates that work
        // would show up here.
        await AssertNoServerSideEffectsAsync();
    }

    [TimedFact(60_000)] // real DB Start/Stop roundtrip + 3 assertion queries; CI contention on SS can push this past 30s.
    public async Task DispatcherHost_UseDispatcherTrue_CompletesLifecycleWithoutThrowing()
    {
        // Arrange
        var state = PopulateState(groupEntityId: Guid.NewGuid(), workerCount: 1);
        var host = CreateDispatcherHost(useDispatcher: true, state);

        // Act — the dispatcher and its workers actually start (scope factory points at the real
        // test DB), poll briefly, then stop. The full job-processing loop is covered by integration
        // tests; here we just pin the DI wiring + StartAsync/StopAsync round-trip.
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await host.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert — host consumes state, doesn't produce it; no Server/Worker/WorkerGroup rows.
        await AssertNoServerSideEffectsAsync();
    }

    [TimedFact]
    public async Task SingleWorkerHost_UseDispatcherTrue_IsSilentNoOp()
    {
        // Arrange
        var state = PopulateState(groupEntityId: Guid.NewGuid(), workerCount: 1);
        var host = CreateSingleWorkerHost(useDispatcher: true, state);

        // Act
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await host.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        await AssertNoServerSideEffectsAsync();
    }

    [TimedFact(60_000)] // real DB Start/Stop roundtrip + 3 assertion queries; CI contention on SS can push this past 30s.
    public async Task SingleWorkerHost_UseDispatcherFalse_CompletesLifecycleWithoutThrowing()
    {
        // Arrange
        var state = PopulateState(groupEntityId: Guid.NewGuid(), workerCount: 1);
        var host = CreateSingleWorkerHost(useDispatcher: false, state);

        // Act
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await host.StopAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        await AssertNoServerSideEffectsAsync();
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
            TestTasks.NullSignals,
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
            TestTasks.NullSignals,
            NullLoggerFactory.Instance);
    }

    private async Task AssertNoServerSideEffectsAsync()
    {
        var ctx = _fixture.CreateContext();
        var servers = await ctx.Set<Server>().CountAsync(Xunit.TestContext.Current.CancellationToken);
        servers.ShouldBe(0, "Worker hosts must consume ServerRegistrationState — not duplicate JoblyServerRegistration's DB inserts.");

        var workerGroups = await ctx.Set<Jobly.Core.Data.Entities.WorkerGroup>().CountAsync(Xunit.TestContext.Current.CancellationToken);
        workerGroups.ShouldBe(0);

        var workers = await ctx.Set<Jobly.Core.Data.Entities.Worker>().CountAsync(Xunit.TestContext.Current.CancellationToken);
        workers.ShouldBe(0);
    }

    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());

        // Lifecycle smoke test — the dispatcher and single-worker both poll in their
        // ExecuteAsync loops. Using real hand-SQL queries means each poll fires an
        // UPDATE ... OUTPUT INSERTED.* against SQL Server, and cancelling that in-flight
        // command on shutdown forces a round-trip for the server to acknowledge the abort
        // (Error 3980). On a loaded CI container that can take seconds, pushing the test
        // past its 30s TimedFact budget. A no-op fake returns empty instantly, so shutdown
        // only has to unwind WaitAsync / Task.Delay — millisecond timescale.
        services.AddSingleton<IJoblySqlQueries<TestContext>>(new NoopSqlQueries());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>
    /// No-op implementation used only by the lifecycle smoke tests in this file. Any method
    /// called by a code path actually under test throws — the fetch-methods return empty so
    /// the polling loops observe "no work" without ever hitting the database.
    /// </summary>
    private sealed class NoopSqlQueries : IJoblySqlQueries<TestContext>
    {
        public Task<List<Job>> ClaimEnqueuedJobsAsync(TestContext context, string[] queues, Guid workerId, DateTime now, int limit, CancellationToken ct) =>
            Task.FromResult(new List<Job>());

        public Task<Job?> LockNextEnqueuedMessageAsync(TestContext context, CancellationToken ct) =>
            Task.FromResult<Job?>(null);

        public Task<List<Job>> LockStaleProcessingJobsAsync(TestContext context, DateTime cutoff, CancellationToken ct) =>
            Task.FromResult(new List<Job>());

        public Task<Job?> LockJobByIdAsync(TestContext context, Guid jobId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Job?> LockJobByIdWaitAsync(TestContext context, Guid jobId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<List<Server>> LockAllServersAsync(TestContext context, CancellationToken ct) =>
            Task.FromResult(new List<Server>());
    }

    private static ServerRegistrationState PopulateState(Guid groupEntityId, int workerCount)
    {
        var state = new ServerRegistrationState();
        var workerIds = Enumerable.Range(0, workerCount).Select(_ => Guid.NewGuid()).ToList();

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
