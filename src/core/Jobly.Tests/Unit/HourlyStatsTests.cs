using System.Text.Json;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Jobly.Worker.Services;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

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
        ctx.Set<Jobly.Core.Data.Entities.Worker>().Add(new Jobly.Core.Data.Entities.Worker
        {
            Id = WorkerId,
            ServerId = ServerId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private JoblyWorkerService<TestContext> CreateWorker()
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(HourlyStatsTestsBase).Assembly);
        services.AddPipelineBehaviors(typeof(HourlyStatsTestsBase).Assembly);
        services.AddLogging();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());
        services.AddSingleton<CounterService>();
        services.AddSingleton<MultiHandlerCounter>();
        services.AddScoped<Jobly.Core.Handlers.JobContext>();
        services.AddScoped<Jobly.Core.Handlers.IJobContext>(x => x.GetRequiredService<Jobly.Core.Handlers.JobContext>());

        var workerConfig = new OptionsWrapper<JoblyWorkerConfiguration>(new JoblyWorkerConfiguration
        {
            WorkerCount = 1,
            ServerId = ServerId,
            Queues = DefaultQueues,
        });
        services.AddSingleton<IOptions<JoblyWorkerConfiguration>>(workerConfig);
        services.AddSingleton<IOptions<JoblyConfiguration>>(workerConfig);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var groupConfig = new WorkerGroupConfiguration
        {
            WorkerCount = 1,
            Queues = DefaultQueues,
        };

        return new JoblyWorkerService<TestContext>(
            WorkerId,
            scopeFactory,
            new NullLogger<JoblyWorkerService<TestContext>>(),
            workerConfig,
            groupConfig,
            TimeProvider.System,
            new FakeLockProvider());
    }

    [Fact]
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
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        await CounterAggregatorTask<TestContext>.AggregateCounters(_fixture.CreateContext());

        // Assert
        var hourKey = $"stats:succeeded:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        var stat = await _fixture.CreateContext().Set<Statistic>()
            .FindAsync(hourKey);

        stat.ShouldNotBeNull();
        stat.Value.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
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
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        await CounterAggregatorTask<TestContext>.AggregateCounters(_fixture.CreateContext());

        // Assert
        var hourKey = $"stats:failed:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        var stat = await _fixture.CreateContext().Set<Statistic>()
            .FindAsync(hourKey);

        stat.ShouldNotBeNull();
        stat.Value.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
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

        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);
        await worker.GetAndProcessJob(CancellationToken.None);
        await worker.GetAndProcessJob(CancellationToken.None);

        await CounterAggregatorTask<TestContext>.AggregateCounters(_fixture.CreateContext());

        // Assert
        var hourKey = $"stats:succeeded:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        var stat = await _fixture.CreateContext().Set<Statistic>()
            .FindAsync(hourKey);

        stat.ShouldNotBeNull();
        stat.Value.ShouldBe(3);
    }

    [Fact]
    public async Task GetJoblyStatus_IncludesHistoricalTotals()
    {
        // Arrange — insert stats
        var ctx = _fixture.CreateContext();
        ctx.Set<Statistic>().Add(new Statistic { Key = "stats:succeeded", Value = 42 });
        ctx.Set<Statistic>().Add(new Statistic { Key = "stats:failed", Value = 7 });
        ctx.Set<Statistic>().Add(new Statistic { Key = "stats:deleted", Value = 3 });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new DashboardStatsService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var status = await svc.GetJoblyStatus();

        // Assert
        status.TotalSucceeded.ShouldBe(42);
        status.TotalFailed.ShouldBe(7);
        status.TotalDeleted.ShouldBe(3);
    }
}

[Collection<PostgreSqlCollection>]
public class HourlyStatsTests_PostgreSql : HourlyStatsTestsBase
{
    public HourlyStatsTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class HourlyStatsTests_SqlServer : HourlyStatsTestsBase
{
    public HourlyStatsTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
