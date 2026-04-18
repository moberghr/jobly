using System.Text.Json;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Core;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class PriorityTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();

    protected PriorityTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

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

    private JoblyWorkerService<TestContext> CreateWorker(string[]? queues = null)
    {
        var effectiveQueues = queues ?? ["a-critical", "b-default", "default"];

        var services = new ServiceCollection();
        services.AddHandlers(typeof(PriorityTestsBase).Assembly);
        services.AddPipelineBehaviors(typeof(PriorityTestsBase).Assembly);
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
            Queues = effectiveQueues,
        });
        services.AddSingleton<IOptions<JoblyWorkerConfiguration>>(workerConfig);
        services.AddSingleton<IOptions<JoblyConfiguration>>(workerConfig);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var groupConfig = new WorkerGroupConfiguration
        {
            WorkerCount = 1,
            Queues = effectiveQueues,
        };

        return new JoblyWorkerService<TestContext>(
            WorkerId,
            scopeFactory,
            new NullLogger<JoblyWorkerService<TestContext>>(),
            workerConfig,
            groupConfig,
            TimeProvider.System);
    }

    [TimedFact]
    public async Task GetAndProcessJob_MultipleQueues_ProcessesAlphabeticalFirst()
    {
        // Arrange — insert jobs in "b-default" and "a-critical"
        var ctx = _fixture.CreateContext();
        var criticalJobId = Guid.NewGuid();
        var defaultJobId = Guid.NewGuid();

        // Insert "b-default" job first (should be processed second)
        ctx.Set<Job>().Add(new Job
        {
            Id = defaultJobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "b-default",
        });

        // Insert "a-critical" job second (should be processed first due to alphabetical queue order)
        ctx.Set<Job>().Add(new Job
        {
            Id = criticalJobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "a-critical",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(["a-critical", "b-default"]);

        // Act — process only 1 job
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert — "a-critical" job should be processed first
        var readCtx = _fixture.CreateContext();
        var criticalJob = await readCtx.Set<Job>().FirstAsync(j => j.Id == criticalJobId);
        criticalJob.CurrentState.ShouldBe(State.Completed);

        var defaultJob = await readCtx.Set<Job>().FirstAsync(j => j.Id == defaultJobId);
        defaultJob.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task GetAndProcessJob_SameQueue_ProcessesEarlierScheduledFirst()
    {
        // Arrange — insert 2 jobs same queue, different ScheduleTime
        var ctx = _fixture.CreateContext();
        var earlierJobId = Guid.NewGuid();
        var laterJobId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = laterJobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddSeconds(-1),
            Queue = "default",
        });

        ctx.Set<Job>().Add(new Job
        {
            Id = earlierJobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new UnitRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddSeconds(-10),
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(["default"]);

        // Act — process only 1 job
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert — earlier scheduled job should be processed first
        var readCtx = _fixture.CreateContext();
        var earlierJob = await readCtx.Set<Job>().FirstAsync(j => j.Id == earlierJobId);
        earlierJob.CurrentState.ShouldBe(State.Completed);

        var laterJob = await readCtx.Set<Job>().FirstAsync(j => j.Id == laterJobId);
        laterJob.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task GetAndProcessJob_DefaultQueue_Processed()
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

        var worker = CreateWorker(["default"]);

        // Act
        var result = await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        result.ShouldBeTrue();
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }

    [TimedFact]
    public async Task GetAndProcessJob_PastScheduledJob_Processed()
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
            CreateTime = DateTime.UtcNow.AddHours(-2),
            ScheduleTime = DateTime.UtcNow.AddHours(-1),
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(["default"]);

        // Act
        var result = await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        result.ShouldBeTrue();
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }
}
