using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Jobly.Worker;
using Jobly.Worker.Retry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobly.Tests.Integration;

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

            await Task.Delay(50);
        }

        throw new TimeoutException($"PauseStateHolder did not reach expected state (paused={expectedPaused}) for group {groupId} within {timeout ?? TimeSpan.FromSeconds(5)}");
    }

    public static Task<JoblyTestServer> StartAsync(IDatabaseFixture fixture)
    {
        return StartAsync(fixture, configure: null);
    }

    public static async Task<JoblyTestServer> StartAsync(IDatabaseFixture fixture, Action<JoblyWorkerConfiguration>? configure)
    {
        var tempCtx = fixture.CreateContext();
        var connectionString = tempCtx.Database.GetConnectionString()!;
        var isPostgres = tempCtx.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;
        await tempCtx.DisposeAsync();

        var host = Host.CreateDefaultBuilder()
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
                    config.UseDispatcher = false;

                    configure?.Invoke(config);
                });

                services.AddJoblyRetry(o =>
                {
                    o.MaxRetries = 3;
                    o.Delays = [1];
                });
            })
            .Build();

        await host.StartAsync();

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
        var serverExists = await context.Set<Server>().AnyAsync(s => s.Id == config.ServerId);
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

        await context.SaveChangesAsync();
    }

    public async Task WaitForJobState(Guid jobId, State state, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var currentState = await CreateContext().Set<Job>()
                .Where(x => x.Id == jobId)
                .Select(x => x.CurrentState)
                .FirstOrDefaultAsync();

            if (currentState == state)
            {
                return;
            }

            await Task.Delay(100);
        }

        var finalState = await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .Select(x => x.CurrentState)
            .FirstOrDefaultAsync();

        throw new TimeoutException($"Job {jobId} did not reach state {state} within {timeout ?? TimeSpan.FromSeconds(10)}. Current state: {finalState}");
    }

    public async Task WaitForJobLog(Guid jobId, string eventType, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var hasLog = await CreateContext().Set<JobLog>()
                .AnyAsync(x => x.JobId == jobId && x.EventType == eventType);

            if (hasLog)
            {
                return;
            }

            await Task.Delay(200);
        }

        var logs = await GetJobLogs(jobId);
        var eventTypes = string.Join(", ", logs.Select(l => l.EventType));
        throw new TimeoutException($"Job {jobId} did not get log event '{eventType}' within {timeout ?? TimeSpan.FromSeconds(10)}. Events: {eventTypes}");
    }

    public async Task WaitForCompletion(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            var ctx = CreateContext();

            var activeJobs = await ctx.Set<Job>()
                .CountAsync(j =>
                    j.CurrentState == State.Enqueued ||
                    j.CurrentState == State.Processing ||
                    j.CurrentState == State.Awaiting);

            var activeMessages = await ctx.Set<Job>()
                .Where(j => j.Kind == JobKind.Message)
                .CountAsync(m => m.CurrentState != State.Completed && m.CurrentState != State.Failed);

            if (activeJobs == 0 && activeMessages == 0)
            {
                return;
            }

            await Task.Delay(200);
        }

        var debugCtx = CreateContext();
        var stuck = await debugCtx.Set<Job>()
            .Where(j => j.CurrentState == State.Enqueued || j.CurrentState == State.Processing || j.CurrentState == State.Awaiting)
            .Select(j => new { j.Id, j.Kind, j.CurrentState, j.ParentJobId })
            .Take(10)
            .ToListAsync();
        var stuckInfo = string.Join(", ", stuck.Select(s => $"{s.Kind}:{s.CurrentState}(parent={s.ParentJobId})"));

        throw new TimeoutException($"Not all jobs completed within timeout. Stuck: {stuckInfo}");
    }

    public async Task<List<JobLog>> GetJobLogs(Guid jobId)
    {
        return await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Timestamp)
            .AsNoTracking()
            .ToListAsync();
    }

    public T GetService<T>()
        where T : notnull
        => _host.Services.GetRequiredService<T>();

    public async Task<Job> GetJob(Guid jobId)
    {
        return await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .AsNoTracking()
            .FirstAsync();
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _host.StopAsync();
        _host.Dispose();
    }
}
