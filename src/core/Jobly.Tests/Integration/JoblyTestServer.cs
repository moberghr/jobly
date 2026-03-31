using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Services;
using Jobly.Worker;
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
    private readonly Func<TestContext> _createContext;

    private JoblyTestServer(IHost host, Func<TestContext> createContext)
    {
        _host = host;
        _createContext = createContext;
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

    public TestContext CreateContext() => _createContext();

    public IJobCommandService CreateCommandService()
    {
        var scope = _host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJobCommandService>();
    }

    public static async Task<JoblyTestServer> StartAsync(Func<TestContext> createContext)
    {
        var tempCtx = createContext();
        var connectionString = tempCtx.Database.GetConnectionString()!;
        tempCtx.Dispose();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<TestContext>(options =>
                    options.UseNpgsql(connectionString)
                        .UseSnakeCaseNamingConvention());

                services.AddJobHandlers(typeof(JoblyTestServer).Assembly);
                services.AddPipelineBehaviors(typeof(JoblyTestServer).Assembly);
                services.AddSingleton<TestData.Handlers.CounterService>();
                services.AddSingleton<TestData.Handlers.MultiHandlerCounter>();

                services.AddJoblyWorker<TestContext>(config =>
                {
                    config.WorkerCount = 5;
                    config.Queues = ["a-critical", "b-default", "c-low", "default", "high"];
                    config.PollingInterval = TimeSpan.FromMilliseconds(100);
                    config.CancellationCheckInterval = TimeSpan.FromSeconds(1);
                    config.OrchestrationInterval = TimeSpan.FromMilliseconds(100);
                    config.MessageRoutingInterval = TimeSpan.FromMilliseconds(500);
                    config.InvisibilityTimeout = TimeSpan.FromMinutes(1);
                    config.HealthCheckInterval = TimeSpan.FromSeconds(30);
                    config.UseDispatcher = false;
                });
            })
            .Build();

        await host.StartAsync();

        return new JoblyTestServer(host, createContext);
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

    public async Task<Job> GetJob(Guid jobId)
    {
        return await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .AsNoTracking()
            .FirstAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
