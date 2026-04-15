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

public abstract class RetentionEdgeCaseTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly string[] DefaultQueues = ["default"];

    protected RetentionEdgeCaseTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
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

    public Task DisposeAsync() => Task.CompletedTask;

    private JoblyWorkerService<TestContext> CreateWorker()
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(RetentionEdgeCaseTestsBase).Assembly);
        services.AddPipelineBehaviors(typeof(RetentionEdgeCaseTestsBase).Assembly);
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
    public async Task GetAndProcessJob_FailedJobWithRetries_StatNotIncrementedDuringRetry()
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
            MaxRetries = 2,
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act — process once (should be requeued, not yet failed)
        await worker.GetAndProcessJob(CancellationToken.None);

        await CounterAggregatorTask<TestContext>.AggregateCounters(_fixture.CreateContext());

        // Assert — stats:failed should be 0 during retry phase
        var failedStat = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        failedStat.ShouldBe(0);
    }

    [Fact]
    public async Task ExpirationCleanup_StatisticsSurviveCleanup()
    {
        // Arrange — insert expired job and add stats
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddHours(-1),
        });
        ctx.Set<Statistic>().Add(new Statistic { Key = "stats:succeeded", Value = 10 });
        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System);

        // Assert — job is deleted, but statistics survive
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
        job.ShouldBeNull();

        var stat = await readCtx.Set<Statistic>().FindAsync("stats:succeeded");
        stat.ShouldNotBeNull();
        stat.Value.ShouldBe(10);
    }

    [Fact]
    public async Task DeleteJob_CompletedJob_DecrementsSucceededAndIncrementsDeleted()
    {
        // Arrange — create a completed job with existing stats
        var ctx = _fixture.CreateContext();
        var publisher = new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, new ServiceCollection().BuildServiceProvider());
        var jobId = await publisher.Enqueue(new UnitRequest());
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        await CounterAggregatorTask<TestContext>.AggregateCounters(_fixture.CreateContext());

        var succeededBefore = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var deletedBefore = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));
        await svc.DeleteJob(jobId);

        await CounterAggregatorTask<TestContext>.AggregateCounters(_fixture.CreateContext());

        // Assert
        var succeededAfter = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var deletedAfter = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        succeededAfter.ShouldBe(succeededBefore - 1);
        deletedAfter.ShouldBe(deletedBefore + 1);
    }

    [Fact]
    public async Task RequeueJob_FailedJob_DecrementsFailedStat()
    {
        // Arrange — create and process a failing job
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
        await worker.GetAndProcessJob(CancellationToken.None);

        await CounterAggregatorTask<TestContext>.AggregateCounters(_fixture.CreateContext());

        var failedBefore = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));
        await svc.RequeueJob(jobId);

        await CounterAggregatorTask<TestContext>.AggregateCounters(_fixture.CreateContext());

        // Assert
        var failedAfter = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        failedAfter.ShouldBe(failedBefore - 1);
    }
}

[Collection("PostgreSql")]
public class RetentionEdgeCaseTests_PostgreSql : RetentionEdgeCaseTestsBase
{
    public RetentionEdgeCaseTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class RetentionEdgeCaseTests_SqlServer : RetentionEdgeCaseTestsBase
{
    public RetentionEdgeCaseTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
