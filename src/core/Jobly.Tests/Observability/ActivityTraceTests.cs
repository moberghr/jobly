using System.Diagnostics;
using System.Text.Json;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Logging;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Observability;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class ActivityTraceTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly string[] DefaultQueues = ["default"];

    protected ActivityTraceTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

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

    private (JoblyWorkerService<TestContext> Worker, ActivityCapture Capture) CreateWorker()
    {
        var capture = new ActivityCapture();
        var services = new ServiceCollection();
        services.AddHandlers(typeof(ActivityTraceTestsBase).Assembly);
        services.AddPipelineBehaviors(typeof(ActivityTraceTestsBase).Assembly);
        services.AddLogging(builder => builder.AddProvider(new JobLoggerProvider()));
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());
        services.AddSingleton<CounterService>();
        services.AddSingleton<MultiHandlerCounter>();
        services.AddSingleton(capture);
        services.AddScoped<JobContext>();
        services.AddScoped<IJobContext>(x => x.GetRequiredService<JobContext>());

        var workerConfig = new OptionsWrapper<JoblyWorkerConfiguration>(new JoblyWorkerConfiguration
        {
            WorkerCount = 1,
            ServerId = ServerId,
            Queues = DefaultQueues,
            EnableHandlerLogging = true,
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

        var worker = new JoblyWorkerService<TestContext>(
            WorkerId,
            scopeFactory,
            new NullLogger<JoblyWorkerService<TestContext>>(),
            workerConfig,
            groupConfig,
            TimeProvider.System);

        return (worker, capture);
    }

    [TimedFact]
    public async Task GetAndProcessJob_JobWithTraceId_ActivityHasMatchingTraceId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ActivityCaptureRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ActivityCaptureRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            TraceId = traceId,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var (worker, capture) = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        capture.TraceId.ShouldBe(traceId.ToString("N"));
    }

    [TimedFact]
    public async Task GetAndProcessJob_JobWithTraceId_ActivityHasNonEmptySpanId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ActivityCaptureRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ActivityCaptureRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            TraceId = Guid.NewGuid(),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var (worker, capture) = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        capture.SpanId.ShouldNotBeNullOrEmpty();
        capture.SpanId.ShouldNotBe("0000000000000000");
    }

    [TimedFact]
    public async Task GetAndProcessJob_JobWithoutTraceId_ActivityUsesJobIdAsTraceId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ActivityCaptureRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ActivityCaptureRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            TraceId = null,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var (worker, capture) = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        capture.TraceId.ShouldBe(jobId.ToString("N"));
    }

    [TimedFact]
    public async Task GetAndProcessJob_JobWithParentSpanId_ActivityHasMatchingParentId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        var parentSpanId = ActivitySpanId.CreateRandom().ToHexString();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ActivityCaptureRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ActivityCaptureRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            TraceId = Guid.NewGuid(),
            ParentSpanId = parentSpanId,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var (worker, capture) = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        capture.ParentSpanId.ShouldBe(parentSpanId);
    }

    [TimedFact]
    public async Task GetAndProcessJob_JobWithoutParentSpanId_ActivityHasEmptyParentId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ActivityCaptureRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ActivityCaptureRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            TraceId = Guid.NewGuid(),
            ParentSpanId = null,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var (worker, capture) = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        capture.ParentSpanId.ShouldBe("0000000000000000");
    }

    [TimedFact]
    public async Task GetAndProcessJob_AfterExecution_ActivityIsCleared()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ActivityCaptureRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ActivityCaptureRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            TraceId = Guid.NewGuid(),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var (worker, _) = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        Activity.Current.ShouldBeNull();
    }

    [TimedFact]
    public async Task GetAndProcessJob_HandlerThrows_ActivityIsCleared()
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
            TraceId = Guid.NewGuid(),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var (worker, _) = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        Activity.Current.ShouldBeNull();
    }

    [TimedFact]
    public async Task GetAndProcessJob_TwoJobs_EachGetsUniqueSpanId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var traceId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ActivityCaptureRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ActivityCaptureRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            TraceId = traceId,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ActivityCaptureRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ActivityCaptureRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddMilliseconds(1),
            Queue = "default",
            TraceId = traceId,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Use shared capture — second execution overwrites first
        var (worker, capture) = CreateWorker();

        // Act — process first job
        await worker.GetAndProcessJob(CancellationToken.None);
        var firstSpanId = capture.SpanId;

        // Process second job
        await worker.GetAndProcessJob(CancellationToken.None);
        var secondSpanId = capture.SpanId;

        // Assert — same trace, different spans
        firstSpanId.ShouldNotBeNullOrEmpty();
        secondSpanId.ShouldNotBeNullOrEmpty();
        firstSpanId.ShouldNotBe(secondSpanId);
    }

    [TimedFact]
    public async Task GetAndProcessJob_Completed_ActivityHasMessagingAttributes()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ActivityCaptureRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ActivityCaptureRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            TraceId = Guid.NewGuid(),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var (worker, capture) = CreateWorker();

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        capture.Tags["messaging.system"].ShouldBe("jobly");
        capture.Tags["messaging.operation.name"].ShouldBe("process");
        capture.Tags["messaging.destination.name"].ShouldBe("default");
        capture.Tags["messaging.message.id"].ShouldBe(jobId.ToString());
        capture.Tags["jobly.job.kind"].ShouldBe("Job");
        capture.Tags["jobly.job.type"].ShouldNotBeNull();
    }
}
