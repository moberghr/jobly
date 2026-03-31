using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Services;
using Jobly.Worker;
using Jobly.Worker.Services;
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
    private readonly ServiceProvider _serviceProvider;
    private readonly List<IHostedService> _hostedServices = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<TestContext> _createContext;

    private JoblyTestServer(ServiceProvider serviceProvider, Func<TestContext> createContext)
    {
        _serviceProvider = serviceProvider;
        _createContext = createContext;
    }

    public IPublisher CreatePublisher()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPublisher>();
    }

    public IBatchPublisher CreateBatchPublisher()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IBatchPublisher>();
    }

    public TestContext CreateContext() => _createContext();

    public IJobCommandService CreateCommandService()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJobCommandService>();
    }

    public static async Task<JoblyTestServer> StartAsync(Func<TestContext> createContext)
    {
        var services = new ServiceCollection();

        // Register DbContext with the same connection as the fixture
        services.AddScoped<TestContext>(sp => createContext());
        services.AddDbContext<TestContext>(options =>
        {
            var ctx = createContext();
            var connString = ctx.Database.GetConnectionString();
            ctx.Dispose();
            options.UseNpgsql(connString)
                .UseSnakeCaseNamingConvention();
        });

        // Register handlers from this test assembly
        services.AddJobHandlers(typeof(JoblyTestServer).Assembly);
        services.AddPipelineBehaviors(typeof(JoblyTestServer).Assembly);

        // Register Jobly worker with fast intervals for testing
        services.AddJoblyWorker<TestContext>(config =>
        {
            config.WorkerCount = 2;
            config.Queues = ["a-critical", "b-default", "c-low", "default", "high"];
            config.PollingInterval = TimeSpan.FromMilliseconds(100);
            config.CancellationCheckInterval = TimeSpan.FromSeconds(1);
            config.OrchestrationInterval = TimeSpan.FromSeconds(1);
            config.MessageRoutingInterval = TimeSpan.FromMilliseconds(500);
            config.InvisibilityTimeout = TimeSpan.FromMinutes(1);
            config.HealthCheckInterval = TimeSpan.FromSeconds(30);
            config.UseDispatcher = false;
        });

        var provider = services.BuildServiceProvider();
        var server = new JoblyTestServer(provider, createContext);

        // Start all hosted services
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var svc in hostedServices)
        {
            await svc.StartAsync(server._cts.Token);
            server._hostedServices.Add(svc);
        }

        return server;
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

    public async Task WaitForCompletion(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            var pending = await CreateContext().Set<Job>()
                .Where(x => x.Kind == JobKind.Job)
                .Where(x => x.CurrentState == State.Enqueued || x.CurrentState == State.Processing || x.CurrentState == State.Awaiting)
                .CountAsync();

            if (pending == 0)
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Not all jobs completed within timeout");
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

    public async Task<List<JobLog>> GetJobLogs(Guid jobId)
    {
        return await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Timestamp)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<Job> GetJob(Guid jobId)
    {
        return await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .AsNoTracking()
            .FirstAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        foreach (var svc in _hostedServices)
        {
            try
            {
                await svc.StopAsync(CancellationToken.None);
            }
            catch
            {
                // Ignore shutdown errors
            }
        }

        _cts.Dispose();
        await _serviceProvider.DisposeAsync();
    }
}
