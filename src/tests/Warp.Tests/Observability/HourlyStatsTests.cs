using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Warp.Core;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Handlers;
using Warp.Core.Handlers.Generated;
using Warp.Core.Services;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;
using Warp.Worker;
using Warp.Worker.Services;

namespace Warp.Tests.Observability;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class HourlyStatsTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly string[] DefaultQueues = ["default"];

    protected HourlyStatsTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();

        var ctx = _fixture.CreateContext();
        ctx.Set<Server>().Add(new Server
        {
            Id = ServerId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
            ServiceCount = 1,
        });
        ctx.Set<Warp.Core.Data.Entities.Worker>().Add(new Warp.Core.Data.Entities.Worker
        {
            Id = WorkerId,
            ServerId = ServerId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private WarpWorkerService<TestContext> CreateWorker()
    {
        var services = new ServiceCollection();
        services.AddWarpMediator();
        services.AddLogging();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());
        services.AddSingleton<CounterService>();
        services.AddSingleton<MultiHandlerCounter>();
        services.AddScoped<Warp.Core.Handlers.JobContext>();
        services.AddScoped<Warp.Core.Handlers.IJobContext>(x => x.GetRequiredService<Warp.Core.Handlers.JobContext>());

        var workerConfig = new OptionsWrapper<WarpWorkerConfiguration>(new WarpWorkerConfiguration
        {
            WorkerCount = 1,
            ServerId = ServerId,
            Queues = DefaultQueues,
        });
        services.AddSingleton<IOptions<WarpWorkerConfiguration>>(workerConfig);
        services.AddSingleton<IOptions<WarpConfiguration>>(workerConfig);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var groupConfig = new WorkerGroupConfiguration
        {
            WorkerCount = 1,
            Queues = DefaultQueues,
        };

        return new WarpWorkerService<TestContext>(
            WorkerId,
            scopeFactory,
            new NullLogger<WarpWorkerService<TestContext>>(),
            workerConfig,
            groupConfig,
            TimeProvider.System,
            Warp.Tests.Helpers.TestTasks.QueriesFromScope<TestContext>(scopeFactory),
            Warp.Tests.Helpers.TestTasks.NullTransport,
            Warp.Tests.Helpers.TestTasks.NullSignals);
    }

    [TimedFact]
    public async Task GetAndProcessJob_CompletedJob_IncrementsHourlySucceededStat()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var worker = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        await Warp.Tests.Helpers.TestTasks.CreateCounterAggregator(_fixture.CreateContext()).AggregateCountersAsync(CancellationToken.None);

        // Assert
        var hourKey = $"stats:succeeded:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        var stat = await _fixture.CreateContext().Set<Statistic>()
            .FindAsync([hourKey], Xunit.TestContext.Current.CancellationToken);

        stat.ShouldNotBeNull();
        stat.Value.ShouldBeGreaterThanOrEqualTo(1);
    }

    [TimedFact]
    public async Task GetAndProcessJob_FailedJob_IncrementsHourlyFailedStat()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ThrowExceptionRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ThrowExceptionRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var worker = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        await Warp.Tests.Helpers.TestTasks.CreateCounterAggregator(_fixture.CreateContext()).AggregateCountersAsync(CancellationToken.None);

        // Assert
        var hourKey = $"stats:failed:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        var stat = await _fixture.CreateContext().Set<Statistic>()
            .FindAsync([hourKey], Xunit.TestContext.Current.CancellationToken);

        stat.ShouldNotBeNull();
        stat.Value.ShouldBeGreaterThanOrEqualTo(1);
    }

    [TimedFact]
    public async Task GetAndProcessJob_MultipleJobs_HourlyStatsAccumulate()
    {
        // Arrange — create 3 jobs
        var ctx = _fixture.CreateContext();
        for (var i = 0; i < 3; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Enqueued,
                Type = typeof(UnitRequest).AssemblyQualifiedName,
                Message = JsonSerializer.Serialize(new UnitRequest()),
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
            });
        }

        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var worker = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);
        await worker.GetAndProcessJob(CancellationToken.None);
        await worker.GetAndProcessJob(CancellationToken.None);

        await Warp.Tests.Helpers.TestTasks.CreateCounterAggregator(_fixture.CreateContext()).AggregateCountersAsync(CancellationToken.None);

        // Assert
        var hourKey = $"stats:succeeded:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        var stat = await _fixture.CreateContext().Set<Statistic>()
            .FindAsync([hourKey], Xunit.TestContext.Current.CancellationToken);

        stat.ShouldNotBeNull();
        stat.Value.ShouldBe(3);
    }

    [TimedFact]
    public async Task GetWarpStatus_IncludesHistoricalTotals()
    {
        // Arrange — insert stats
        var ctx = _fixture.CreateContext();
        ctx.Set<Statistic>().Add(new Statistic { Key = "stats:succeeded", Value = 42 });
        ctx.Set<Statistic>().Add(new Statistic { Key = "stats:failed", Value = 7 });
        ctx.Set<Statistic>().Add(new Statistic { Key = "stats:deleted", Value = 3 });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var status = await svc.GetWarpStatus();

        // Assert
        status.TotalSucceeded.ShouldBe(42);
        status.TotalFailed.ShouldBe(7);
        status.TotalDeleted.ShouldBe(3);
    }
}
