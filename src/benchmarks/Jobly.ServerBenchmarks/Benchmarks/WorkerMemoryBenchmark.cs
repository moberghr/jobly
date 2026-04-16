using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.ServerBenchmarks.Infrastructure;
using Jobly.Worker;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.ServerBenchmarks.Benchmarks;

/// <summary>
/// Measures per-job memory allocation of the worker execution path.
/// Calls GetAndProcessJob() directly on the benchmark thread so [MemoryDiagnoser]
/// captures all allocations precisely.
///
/// No hosted services running — isolates the worker path from background task noise.
/// </summary>
[Config(typeof(ComponentBenchmarkConfig))]
public class WorkerMemoryBenchmark
{
    private PostgresServerFixture _fixture = null!;
    private IJoblyWorkerService _workerService = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = new PostgresServerFixture();
        await _fixture.InitializeWithoutHostedServicesAsync();

        // Construct a JoblyWorkerService manually (it's not registered in DI)
        var services = _fixture.Host.Services;
        _workerService = new JoblyWorkerService<TestContext>(
            Guid.NewGuid(),
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<ILogger<JoblyWorkerService<TestContext>>>(),
            services.GetRequiredService<IOptions<JoblyWorkerConfiguration>>(),
            new WorkerGroupConfiguration { Queues = ["default"], WorkerCount = 1 },
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<IDistributedLockProvider>());

        // Register a server + worker in the DB (required for job processing)
        await using var scope = services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
        var now = DateTime.UtcNow;
        var config = services.GetRequiredService<IOptions<JoblyWorkerConfiguration>>().Value;
        ctx.Set<Server>().Add(new Server
        {
            Id = config.ServerId,
            ServerName = "benchmark-server",
            StartedTime = now,
            LastHeartbeatTime = now,
            ServiceCount = 1,
        });
        await ctx.SaveChangesAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _fixture.DisposeAsync();
    }

    [IterationSetup]
    public void InsertJob()
    {
        using var scope = _fixture.Host.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
        var now = DateTime.UtcNow;
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            Type = typeof(EmptyRequest).AssemblyQualifiedName!,
            Message = JsonSerializer.Serialize(new EmptyRequest()),
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
        });
        ctx.SaveChanges();
    }

    /// <summary>
    /// Full worker cycle: fetch job (with row lock + transaction), deserialize,
    /// execute handler, finalize state, write logs + counters. All on the benchmark thread.
    /// </summary>
    [Benchmark]
    public async Task GetAndProcessJob()
    {
        await _workerService.GetAndProcessJob(CancellationToken.None);
    }
}
