using Jobly.Core;
using Jobly.Core.CircuitBreaker;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Mutex;
using Jobly.Core.NoRestart;
using Jobly.Core.Retry;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jobly.Tests.Fixtures;

/// <summary>
/// Boots the full Jobly worker (workers + background tasks) against a real database.
/// Tests can publish jobs and wait for results — like the real app.
/// </summary>
public class JoblyTestServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly IDatabaseFixture _fixture;

    private JoblyTestServer(IHost host, IDatabaseFixture fixture)
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
            var config = _host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JoblyWorkerConfiguration>>().Value;
            return config.ServerId;
        }
    }

    public PauseStateHolder PauseState => _host.Services.GetRequiredService<PauseStateHolder>();

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

    public static Task<JoblyTestServer> StartAsync(IDatabaseFixture fixture)
    {
        return StartAsync(fixture, configure: null);
    }

    public static Task<JoblyTestServer> StartAsync(IDatabaseFixture fixture, Action<JoblyWorkerConfiguration>? configure)
    {
        return StartAsync(fixture, configure, configureServices: null);
    }

    public static async Task<JoblyTestServer> StartAsync(
        IDatabaseFixture fixture,
        Action<JoblyWorkerConfiguration>? configure,
        Action<IServiceCollection>? configureServices)
    {
        var tempCtx = fixture.CreateContext();
        var connectionString = tempCtx.Database.GetConnectionString()!;
        var isPostgres = tempCtx.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;
        await tempCtx.DisposeAsync();

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Silence worker-infrastructure and hosting-lifetime chatter in CI. We can't use
                // SetMinimumLevel(Warning) globally because RealTimeLogIntegrationTests and
                // JobLogTests exercise the handler log capture path, which depends on
                // Information-level logs flowing through JobLoggerProvider to JobLog.
                logging.AddFilter("Microsoft", LogLevel.Warning);
                logging.AddFilter("Jobly.Worker", LogLevel.Warning);
                logging.AddFilter("Jobly.Tests.TestData.Handlers.LoggingPipelineBehavior", LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                services.AddDbContext<TestContext>(options =>
                {
                    if (isPostgres)
                    {
                        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
                    }
                    else
                    {
                        options.UseSqlServer(connectionString);
                    }
                });

                services.AddHandlers(typeof(JoblyTestServer).Assembly);
                services.AddPipelineBehaviors(typeof(JoblyTestServer).Assembly);
                services.AddSingleton<TestData.Handlers.CounterService>();
                services.AddSingleton<TestData.Handlers.MultiHandlerCounter>();
                services.AddSingleton<TestData.Handlers.MetadataCapture>();

                services.AddJoblyWorker<TestContext>(config =>
                {
                    config.WorkerCount = 5;
                    config.Queues = ["a-critical", "b-default", "c-low", "default", "high"];
                    config.PollingInterval = TimeSpan.FromMilliseconds(100);
                    config.CancellationCheckInterval = TimeSpan.FromSeconds(1);
                    config.OrchestrationInterval = TimeSpan.FromMilliseconds(100);
                    config.MessageRoutingInterval = TimeSpan.FromMilliseconds(500);
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

                    configure?.Invoke(config);
                });

                services.AddJoblyRetry(o =>
                {
                    o.MaxRetries = 3;
                    o.Delays = [1];
                });
                services.AddJoblyMutex();
                services.AddJoblyNoRestart();
                services.AddJoblyCircuitBreaker<TestContext>(o =>
                {
                    o.Threshold = 1000;
                    o.Duration = TimeSpan.FromHours(1);
                    o.ResetJitter = TimeSpan.FromSeconds(1);
                });

                configureServices?.Invoke(services);
            })
            .Build();

        await host.StartAsync(Xunit.TestContext.Current.CancellationToken);

        return new JoblyTestServer(host, fixture);
    }

    /// <summary>
    /// Re-registers the server and workers in the DB after Respawn clears all tables.
    /// The host's background services expect these rows to exist.
    /// </summary>
    public async Task ReRegisterServer()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestContext>();

        // Check if server still exists (Respawn may have deleted it)
        var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<JoblyWorkerConfiguration>>().Value;
        var serverExists = await context.Set<Server>().AnyAsync(s => s.Id == config.ServerId, Xunit.TestContext.Current.CancellationToken);
        if (serverExists)
        {
            return;
        }

        var now = DateTime.UtcNow;
        context.Set<Server>().Add(new Server
        {
            Id = config.ServerId,
            ServerName = config.ServerName ?? "test-server",
            StartedTime = now,
            LastHeartbeatTime = now,
            ServiceCount = config.WorkerCount,
        });

        // Re-register workers — get IDs from existing Worker entities if any, otherwise create new ones
        for (var i = 0; i < config.WorkerCount; i++)
        {
            context.Set<Jobly.Core.Data.Entities.Worker>().Add(new Jobly.Core.Data.Entities.Worker
            {
                Id = Guid.NewGuid(),
                ServerId = config.ServerId,
                StartedTime = now,
                LastHeartbeatTime = now,
            });
        }

        await context.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
    }

    public async Task WaitForJobState(Guid jobId, State state, TimeSpan? timeout = null)
    {
        // Default 5s — with idle backoff disabled (see JoblyTestServer.StartAsync), worker
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

        var debugCtx = CreateContext();
        var stuck = await debugCtx.Set<Job>()
            .Where(j => j.CurrentState == State.Enqueued || j.CurrentState == State.Processing || j.CurrentState == State.Awaiting)
            .Select(j => new { j.Id, j.Kind, j.CurrentState, j.ParentJobId })
            .Take(10)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        var stuckInfo = string.Join(", ", stuck.Select(s => $"{s.Kind}:{s.CurrentState}(parent={s.ParentJobId})"));

        throw new TimeoutException($"Not all jobs completed within timeout. Stuck: {stuckInfo}");
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
        await _host.StopAsync(Xunit.TestContext.Current.CancellationToken);
        _host.Dispose();
    }
}
