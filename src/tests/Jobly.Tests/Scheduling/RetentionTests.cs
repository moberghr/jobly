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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Scheduling;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class RetentionTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly string[] DefaultQueues = ["default"];

    protected RetentionTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

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
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private JoblyWorkerService<TestContext> CreateWorker()
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(RetentionTestsBase).Assembly);
        services.AddPipelineBehaviors(typeof(RetentionTestsBase).Assembly);
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
            Jobly.Tests.Helpers.TestTasks.QueriesFromScope<TestContext>(scopeFactory),
            Jobly.Tests.Helpers.TestTasks.NullTransport,
            Jobly.Tests.Helpers.TestTasks.NullSignals);
    }

    [TimedFact]
    public async Task GetAndProcessJob_CompletedJob_SetsExpireAt()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = new Publisher<TestContext>(ctx, TimeProvider.System, new ServiceCollection().BuildServiceProvider());
        var jobId = await publisher.Enqueue(new UnitRequest());
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var worker = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Completed);
        job.ExpireAt.ShouldNotBeNull();
        job.ExpireAt.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [TimedFact]
    public async Task GetAndProcessJob_FailedJob_ExpireAtIsNull()
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

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Failed);
        job.ExpireAt.ShouldBeNull();
    }

    [TimedFact]
    public async Task GetAndProcessJob_CompletedJob_IncrementsSucceededStat()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = new Publisher<TestContext>(ctx, TimeProvider.System, new ServiceCollection().BuildServiceProvider());

        var statBefore = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

        await publisher.Enqueue(new UnitRequest());
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var worker = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        await Jobly.Tests.Helpers.TestTasks.CreateCounterAggregator(_fixture.CreateContext()).AggregateCountersAsync(CancellationToken.None);

        // Assert
        var statAfter = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

        statAfter.ShouldBe(statBefore + 1);
    }

    [TimedFact]
    public async Task GetAndProcessJob_FailedJob_IncrementsFailedStat()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();

        var statBefore = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

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

        await Jobly.Tests.Helpers.TestTasks.CreateCounterAggregator(_fixture.CreateContext()).AggregateCountersAsync(CancellationToken.None);

        // Assert
        var statAfter = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

        statAfter.ShouldBe(statBefore + 1);
    }

    [TimedFact]
    public async Task DeleteJob_IncrementsDeletedStat()
    {
        // Arrange
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
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var statBefore = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.DeleteJob(jobId);

        await Jobly.Tests.Helpers.TestTasks.CreateCounterAggregator(_fixture.CreateContext()).AggregateCountersAsync(CancellationToken.None);

        // Assert
        var statAfter = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

        statAfter.ShouldBe(statBefore + 1);
    }

    [TimedFact]
    public async Task RequeueJob_DecrementsSucceededStat()
    {
        // Arrange — enqueue and process a job so stats:succeeded is incremented
        var ctx = _fixture.CreateContext();
        var publisher = new Publisher<TestContext>(ctx, TimeProvider.System, new ServiceCollection().BuildServiceProvider());
        var jobId = await publisher.Enqueue(new UnitRequest());
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        await Jobly.Tests.Helpers.TestTasks.CreateCounterAggregator(_fixture.CreateContext()).AggregateCountersAsync(CancellationToken.None);

        var succeededBefore = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.RequeueJob(jobId);

        await Jobly.Tests.Helpers.TestTasks.CreateCounterAggregator(_fixture.CreateContext()).AggregateCountersAsync(CancellationToken.None);

        // Assert
        var succeededAfter = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);

        succeededAfter.ShouldBe(succeededBefore - 1);
    }

    [TimedFact]
    public async Task ExpirationCleanup_DeletesExpiredJobAndLogs()
    {
        // Arrange
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
        ctx.Set<JobLog>().Add(new JobLog
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            EventType = "Created",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = "Test log entry",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var cleanCtx = _fixture.CreateContext();
        var cleaned = await Jobly.Tests.Helpers.TestTasks.CreateExpirationCleanup(cleanCtx, TimeProvider.System).RunCleanupAsync(CancellationToken.None);

        // Assert
        cleaned.ShouldBeGreaterThanOrEqualTo(1);

        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldBeNull();

        var logs = await readCtx.Set<JobLog>().Where(l => l.JobId == jobId).ToListAsync(Xunit.TestContext.Current.CancellationToken);
        logs.ShouldBeEmpty();
    }
}
