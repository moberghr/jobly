using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Warp.Core;
using Warp.Core.CircuitBreaker;
using Warp.Core.Concurrency;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Handlers;
using Warp.Core.NoRestart;
using Warp.Core.Retry;
using Warp.Core.Services;
using Warp.Core.Timeout;
using Warp.Provider.PostgreSql;
using Warp.Provider.SqlServer;
using Warp.Tests.Fixtures;
using Warp.Worker;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Boots the full Warp worker (workers + background tasks) against a real database.
/// Tests can publish jobs and wait for results — like the real app.
/// </summary>
public class WarpTestServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly IDatabaseFixture _fixture;

    private WarpTestServer(IHost host, IDatabaseFixture fixture)
    {
        _host = host;
        _fixture = fixture;
    }

    public IPublisher CreatePublisher()
    {
        var scope = _host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPublisher>();
    }

    public IBatchPublisher CreateBatchPublisher()
    {
        var scope = _host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IBatchPublisher>();
    }

    public TestContext CreateContext() => _fixture.CreateContext();

    public IJobCommandService CreateCommandService()
    {
        var scope = _host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJobCommandService>();
    }

    public IServerCommandService CreateServerCommandService()
    {
        var scope = _host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IServerCommandService>();
    }

    public Guid ServerId
    {
        get
        {
            var config = _host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<WarpWorkerConfiguration>>().Value;
            return config.ServerId;
        }
    }

    public PauseStateHolder PauseState => _host.Services.GetRequiredService<PauseStateHolder>();

    /// <summary>
    /// Synchronously runs the Heartbeat task once — refreshing <see cref="PauseStateHolder"/>
    /// from the DB. Use after a Pause/Resume DB write when the test config has disabled the
    /// auto Heartbeat (HealthCheckInterval = null), so that pause propagation is deterministic
    /// rather than dependent on the periodic heartbeat tick.
    /// </summary>
    public async Task<string?> RunHeartbeatOnceAsync(CancellationToken ct = default)
    {
        // Resolve the Heartbeat task in its own scope and call ExecuteAsync directly. This
        // sidesteps the ServerTaskHost auto-loop (which doesn't register tasks whose
        // DefaultInterval is null) so tests that disable HealthCheckInterval can still drive
        // a heartbeat tick deterministically. Heartbeat takes no distributed lock, so running
        // it without the host's lock guard is safe.
        await using var scope = _host.Services.CreateAsyncScope();
        var heartbeat = scope.ServiceProvider
            .GetServices<Warp.Worker.Services.IServerTask>()
            .OfType<Warp.Worker.Services.Heartbeat<TestContext>>()
            .Single();
        return await heartbeat.ExecuteAsync(ct);
    }

    /// <summary>
    /// Synchronously runs the Orchestrator task once — finalizes parents whose children have
    /// all reached terminal state, activates continuations, fails children of deleted parents.
    /// Use after a state change that should trigger orchestration (e.g. a job reaching Failed)
    /// when the test config has disabled the auto Orchestrator (<c>OrchestrationInterval = null</c>),
    /// so that "did/didn't activate the continuation" is decided by an explicit tick rather than
    /// a wall-clock <c>Task.Delay</c>. Bypasses the <see cref="ServerTaskHost{TContext}"/>
    /// distributed-lock guard — only safe when no auto-orchestrator is running concurrently.
    /// </summary>
    public async Task<string?> RunOrchestratorOnceAsync(CancellationToken ct = default)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider
            .GetServices<Warp.Worker.Services.IServerTask>()
            .OfType<Warp.Worker.Services.Orchestrator<TestContext>>()
            .Single();
        return await orchestrator.ExecuteAsync(ct);
    }

    /// <summary>
    /// Synchronously runs the MessageRouter task once — discovers handlers for any
    /// <c>Kind=Message</c> rows in <see cref="State.Enqueued"/> and creates child handler jobs.
    /// Use when the test config has disabled the auto MessageRouter so message routing is
    /// driven by explicit ticks. Bypasses the host lock guard — only safe with the auto loop off.
    /// </summary>
    public async Task<string?> RunMessageRouterOnceAsync(CancellationToken ct = default)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var router = scope.ServiceProvider
            .GetServices<Warp.Worker.Services.IServerTask>()
            .OfType<Warp.Worker.Services.MessageRouter<TestContext>>()
            .Single();
        return await router.ExecuteAsync(ct);
    }

    /// <summary>
    /// Polls until the PauseStateHolder reflects the expected paused/resumed state for a group.
    /// Use instead of Task.Delay after calling pause/resume APIs.
    /// </summary>
    public async Task WaitForPauseState(Guid groupId, bool expectedPaused, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (PauseState.IsPaused(groupId) == expectedPaused)
            {
                return;
            }

            await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"PauseStateHolder did not reach expected state (paused={expectedPaused}) for group {groupId} within {timeout ?? TimeSpan.FromSeconds(5)}");
    }

    public static Task<WarpTestServer> StartAsync(IDatabaseFixture fixture)
    {
        return StartAsync(fixture, configure: null);
    }

    public static Task<WarpTestServer> StartAsync(IDatabaseFixture fixture, Action<WarpWorkerBuilder<TestContext>>? configure)
    {
        return StartAsync(fixture, configure, configureServices: null);
    }

    public static async Task<WarpTestServer> StartAsync(
        IDatabaseFixture fixture,
        Action<WarpWorkerBuilder<TestContext>>? configure,
        Action<IServiceCollection>? configureServices)
    {
        TestLifecycleTrace.Record("WarpTestServer.StartAsync starting");

        TestLifecycleTrace.Record("Fixture.CreateContext (probe) starting");
        var tempCtx = fixture.CreateContext();
        var baseConnectionString = tempCtx.Database.GetConnectionString()!;
        var isPostgres = tempCtx.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;
        await tempCtx.DisposeAsync();
        TestLifecycleTrace.Record("Fixture.CreateContext (probe) returned");

        // For SQL Server only: give each test-server instance its own ADO.NET connection pool by
        // appending a unique Application Name (which Microsoft.Data.SqlClient includes in the pool
        // key). This mirrors production pod-restart (new process = new pool) so that one disposed
        // server's cancelled-mid-flight SqlConnections — left in 'attention-sent' state — don't
        // poison the pool that a replacement server then borrows from. Npgsql doesn't have the
        // same session-on-cancel pathology, and per-server pools blow past PostgreSQL's
        // max_connections under parallel test load, so we skip it there.
        var connectionString = isPostgres
            ? baseConnectionString
            : $"{baseConnectionString};Application Name=warp-test-{Guid.NewGuid():N}";

        TestLifecycleTrace.Record("Host.Build starting");
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Silence worker-infrastructure and hosting-lifetime chatter in CI. We can't use
                // SetMinimumLevel(Warning) globally because RealTimeLogIntegrationTests and
                // JobLogTests exercise the handler log capture path, which depends on
                // Information-level logs flowing through JobLoggerProvider to JobLog.
                logging.AddFilter("Microsoft", LogLevel.Warning);
                logging.AddFilter("Warp.Worker", LogLevel.Warning);
                logging.AddFilter("Warp.Tests.TestData.Handlers.LoggingPipelineBehavior", LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                services.AddDbContext<TestContext>(options =>
                {
                    // Bound every SQL command at 5 seconds in tests. Default ADO.NET command
                    // timeout is 30s — long enough that one hung connection (e.g. attention-sent
                    // state from a cancelled-mid-flight prior request) causes a cascading 10s
                    // [TimedFact] failure with no diagnostic, because the test gives up before
                    // the underlying command does. Capping at 5s makes the bad command fail with
                    // a timeout exception that shows up in logs and lets ServerTaskLoop's catch
                    // handler retry on a fresh connection. Long-running provider-native commands
                    // (Service Broker WAITFOR / LISTEN) set CommandTimeout explicitly on the
                    // SqlCommand / NpgsqlCommand and are unaffected by this default.
                    if (isPostgres)
                    {
                        // The fixture's connection string carries MaxPoolSize so this
                        // per-server EF data source has a bounded pool, keeping the
                        // aggregate connection count under the testcontainer's
                        // max_connections under parallel test-class load.
                        options.UseNpgsql(connectionString, npg => npg.CommandTimeout(5)).UseSnakeCaseNamingConvention();
                    }
                    else
                    {
                        options.UseSqlServer(connectionString, sql => sql.CommandTimeout(5));
                    }
                });

                services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(TestData.Handlers.TestMetadataPublishBehavior<>));
                services.AddSingleton<TestData.Handlers.CounterService>();
                services.AddSingleton<TestData.Handlers.MultiHandlerCounter>();
                services.AddSingleton<TestData.Handlers.MetadataCapture>();

                services.AddWarpWorker<TestContext>(config =>
                {
                    if (isPostgres)
                    {
                        config.UsePostgreSql();
                    }
                    else
                    {
                        config.UseSqlServer();
                    }

                    config.WorkerCount = 5;
                    config.Queues = ["a-critical", "b-default", "c-low", "default", "high"];
                    config.PollingInterval = TimeSpan.FromMilliseconds(100);
                    config.CancellationCheckInterval = TimeSpan.FromSeconds(1);
                    config.OrchestrationInterval = TimeSpan.FromMilliseconds(100);
                    config.MessageRoutingInterval = TimeSpan.FromMilliseconds(100);
                    config.InvisibilityTimeout = TimeSpan.FromMinutes(1);
                    config.HealthCheckInterval = TimeSpan.FromMilliseconds(200);
                    config.LogFlushInterval = TimeSpan.FromMilliseconds(100);

                    // Disable idle-polling backoff in tests: workers must pick up a new job
                    // within PollingInterval, not up to MaxPollingInterval (30s default).
                    // Without this, the first job enqueued after an idle period waits for
                    // the exponentially-grown sleep to expire.
                    config.MaxPollingInterval = TimeSpan.FromMilliseconds(100);
                    config.PollingIntervalFactor = 1.0;

                    // Run stale-recovery fast so crash-recovery tests don't need multi-second
                    // waits. Production default is 30s; 1s is 30x faster without being so
                    // aggressive that it races worker keep-alive refreshes under two-server load.
                    config.StaleJobRecoveryInterval = TimeSpan.FromSeconds(1);

                    // Tests configure short retry delays (1s); keep the Scheduled→Enqueued
                    // sweep tight so retry loops don't stall waiting for the 5s default.
                    config.ScheduledActivationInterval = TimeSpan.FromMilliseconds(250);
                    config.UseDispatcher = false;

                    // Turn off the auto Counter→Statistic aggregator. No test relies on it
                    // firing automatically — tests that need aggregation invoke
                    // TestTasks.CreateCounterAggregator(...).AggregateCountersAsync(...) directly.
                    // Leaving it on creates a 5s race against any test that reads Counter rows.
                    config.CounterAggregationInterval = null;

                    configure?.Invoke(config);

                    config.AddRetry(o =>
                    {
                        o.MaxRetries = 1;
                        o.Delays = [1];
                    });
                    config.AddConcurrency();
                    config.AddNoRestart();
                    config.AddCircuitBreaker(o =>
                    {
                        o.Threshold = 1000;
                        o.Duration = TimeSpan.FromHours(1);
                        o.ResetJitter = TimeSpan.FromSeconds(1);
                    });
                    config.AddTimeout();
                });

                configureServices?.Invoke(services);
            })
            .Build();
        TestLifecycleTrace.Record("Host.Build returned");

        var serverId = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<WarpWorkerConfiguration>>().Value.ServerId;
        ServerLifecycleTrace.Record(serverId, "IHost.StartAsync starting");
        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);
        ServerLifecycleTrace.Record(serverId, "IHost.StartAsync returned");
        TestLifecycleTrace.Record($"WarpTestServer.StartAsync returned (server={serverId})");

        return new WarpTestServer(host, fixture);
    }

    public async Task WaitForJobState(Guid jobId, State state, TimeSpan? timeout = null)
    {
        // Default 5s — with idle backoff disabled (see WarpTestServer.StartAsync), worker
        // pickup is bounded by PollingInterval (100ms). Any wait beyond 5s indicates a real
        // bug, not "needs more time". Tests that exercise slower pipelines (retries with
        // configured delays, stale recovery) pass an explicit larger timeout.
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var deadline = DateTime.UtcNow + effectiveTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var currentState = await CreateContext().Set<Job>()
                .Where(x => x.Id == jobId)
                .Select(x => x.CurrentState)
                .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

            if (currentState == state)
            {
                return;
            }

            await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        }

        var finalState = await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .Select(x => x.CurrentState)
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

        throw new TimeoutException($"Job {jobId} did not reach state {state} within {effectiveTimeout}. Current state: {finalState}");
    }

    public async Task WaitForJobLog(Guid jobId, string eventType, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            var hasLog = await CreateContext().Set<JobLog>()
                .AnyAsync(x => x.JobId == jobId && x.EventType == eventType, Xunit.TestContext.Current.CancellationToken);

            if (hasLog)
            {
                return;
            }

            await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);
        }

        var logs = await GetJobLogs(jobId);
        var eventTypes = string.Join(", ", logs.Select(l => l.EventType));
        throw new TimeoutException($"Job {jobId} did not get log event '{eventType}' within {timeout ?? TimeSpan.FromSeconds(10)}. Events: {eventTypes}");
    }

    /// <summary>
    /// Polls until every job on <paramref name="queue"/> has left the <see cref="State.Enqueued"/>
    /// state (i.e. a worker has picked it up), without waiting for terminal completion.
    /// Use this when the test wants to observe in-flight / buffered state before shutdown.
    /// </summary>
    public async Task WaitForJobsToLeaveEnqueued(string queue, int expectedCount, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var ctx = CreateContext();
            var totalSeen = await ctx.Set<Job>().CountAsync(j => j.Queue == queue, Xunit.TestContext.Current.CancellationToken);
            var stillEnqueued = await ctx.Set<Job>()
                .CountAsync(j => j.Queue == queue && j.CurrentState == State.Enqueued, Xunit.TestContext.Current.CancellationToken);
            if (totalSeen >= expectedCount && stillEnqueued == 0)
            {
                return;
            }

            await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Jobs on queue {queue} did not leave Enqueued within {timeout ?? TimeSpan.FromSeconds(15)}");
    }

    public async Task WaitForCompletion(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            var ctx = CreateContext();

            var activeJobs = await ctx.Set<Job>()
                .CountAsync(
                    j => j.CurrentState == State.Enqueued
                        || j.CurrentState == State.Scheduled
                        || j.CurrentState == State.Processing
                        || j.CurrentState == State.Awaiting,
                    Xunit.TestContext.Current.CancellationToken);

            var activeMessages = await ctx.Set<Job>()
                .Where(j => j.Kind == JobKind.Message)
                .CountAsync(m => m.CurrentState != State.Completed && m.CurrentState != State.Failed, Xunit.TestContext.Current.CancellationToken);

            if (activeJobs == 0 && activeMessages == 0)
            {
                return;
            }

            await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(await _fixture.DumpDiagnosticsAsync(
            $"Not all jobs completed within {(timeout ?? TimeSpan.FromSeconds(30)).TotalSeconds:0.#}s.",
            Xunit.TestContext.Current.CancellationToken));
    }

    public async Task<List<JobLog>> GetJobLogs(Guid jobId)
    {
        return await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Timestamp)
            .AsNoTracking()
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
    }

    public T GetService<T>()
        where T : notnull
        => _host.Services.GetRequiredService<T>();

    public async Task<Job> GetJob(Guid jobId)
    {
        return await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .AsNoTracking()
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        ServerLifecycleTrace.Record(ServerId, "IHost.StopAsync starting");
        await _host.StopAsync(Xunit.TestContext.Current.CancellationToken);
        ServerLifecycleTrace.Record(ServerId, "IHost.StopAsync returned");
        _host.Dispose();
    }
}
