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

namespace Jobly.Tests.Worker;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class HandlerLogTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly string[] DefaultQueues = ["default"];

    protected HandlerLogTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

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

    private JoblyWorkerService<TestContext> CreateWorker(bool enableHandlerLogging = true)
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(HandlerLogTestsBase).Assembly);
        services.AddPipelineBehaviors(typeof(HandlerLogTestsBase).Assembly);
        services.AddLogging(builder => builder.AddProvider(new JobLoggerProvider()));
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
            EnableHandlerLogging = enableHandlerLogging,
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
    public async Task GetAndProcessJob_HandlerWithLogging_LogsAreCaptured()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(LoggingRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new LoggingRequest()),
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
        var logs = await readCtx.Set<JobLog>()
            .Where(l => l.JobId == jobId && l.EventType == "Log")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        logs.Count.ShouldBeGreaterThanOrEqualTo(2);
        logs.ShouldContain(l => l.Message.Contains("Processing logging request", StringComparison.Ordinal));
        logs.ShouldContain(l => l.Message.Contains("This is a warning", StringComparison.Ordinal));
    }

    [TimedFact]
    public async Task GetAndProcessJob_HandlerWithLogging_LogsHaveCorrectLevel()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(LoggingRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new LoggingRequest()),
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
        var logs = await readCtx.Set<JobLog>()
            .Where(l => l.JobId == jobId && l.EventType == "Log")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        var infoLog = logs.First(l => l.Message.Contains("Processing logging request", StringComparison.Ordinal));
        infoLog.Level.ShouldBe("Information");

        var warningLog = logs.First(l => l.Message.Contains("This is a warning", StringComparison.Ordinal));
        warningLog.Level.ShouldBe("Warning");
    }

    [TimedFact]
    public async Task GetAndProcessJob_HandlerThatThrows_LogsBeforeErrorAreCaptured()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ErrorLoggingRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ErrorLoggingRequest()),
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
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.CurrentState.ShouldBe(State.Failed);

        var handlerLogs = await readCtx.Set<JobLog>()
            .Where(l => l.JobId == jobId && l.EventType == "Log")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        handlerLogs.ShouldContain(l => l.Message.Contains("About to fail", StringComparison.Ordinal));
    }

    [TimedFact]
    public async Task GetAndProcessJob_TwoJobs_LogsDoNotLeak()
    {
        // Arrange — create two logging jobs
        var ctx = _fixture.CreateContext();
        var jobId1 = Guid.NewGuid();
        var jobId2 = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = jobId1,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(LoggingRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new LoggingRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId2,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ErrorLoggingRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ErrorLoggingRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddMilliseconds(1),
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var worker = CreateWorker();

        // Act — process both jobs sequentially
        await worker.GetAndProcessJob(CancellationToken.None);
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert — each job only has its own logs
        var readCtx = _fixture.CreateContext();

        var logs1 = await readCtx.Set<JobLog>()
            .Where(l => l.JobId == jobId1 && l.EventType == "Log")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        var logs2 = await _fixture.CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId2 && l.EventType == "Log")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        // Job 1 (LoggingRequest) should not contain "About to fail" from ErrorLoggingRequest
        logs1.ShouldNotContain(l => l.Message.Contains("About to fail", StringComparison.Ordinal));

        // Job 2 (ErrorLoggingRequest) should not contain "Processing logging request" from LoggingRequest
        logs2.ShouldNotContain(l => l.Message.Contains("Processing logging request", StringComparison.Ordinal));

        // Each should have its own logs
        logs1.ShouldContain(l => l.Message.Contains("Processing logging request", StringComparison.Ordinal));
        logs2.ShouldContain(l => l.Message.Contains("About to fail", StringComparison.Ordinal));
    }

    [TimedFact]
    public async Task GetAndProcessJob_HandlerLoggingDisabled_HandlerLogsNotWritten()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(LoggingRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new LoggingRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var worker = CreateWorker(enableHandlerLogging: false);

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var handlerLogs = await readCtx.Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .Where(x => x.EventType == "Log")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        handlerLogs.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task GetAndProcessJob_HandlerLoggingDisabled_StateLogsStillWritten()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(LoggingRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new LoggingRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var worker = CreateWorker(enableHandlerLogging: false);

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var allLogs = await readCtx.Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        allLogs.ShouldContain(x => string.Equals(x.EventType, "Processing", StringComparison.Ordinal));
        allLogs.ShouldContain(x => string.Equals(x.EventType, "Completed", StringComparison.Ordinal));
    }
}
