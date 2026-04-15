using System.Text.Json;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Jobly.Core.Retry;
using Jobly.Worker.Services;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class RetryTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly string[] DefaultQueues = ["default"];

    protected RetryTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

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

    private static int GetRetriedTimes(Job job)
    {
        if (job.Metadata == null)
        {
            return 0;
        }

        var meta = MetadataSerializer.Deserialize(job.Metadata);
        if (meta.TryGetValue("RetriedTimes", out var value))
        {
            return Convert.ToInt32(value);
        }

        return 0;
    }

    private JoblyWorkerService<TestContext> CreateWorker(int maxRetries = 3, int[]? delays = null)
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(RetryTestsBase).Assembly);
        services.AddPipelineBehaviors(typeof(RetryTestsBase).Assembly);
        services.AddLogging();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());
        services.AddSingleton<CounterService>();
        services.AddSingleton<MultiHandlerCounter>();
        services.AddScoped<Jobly.Core.Handlers.JobContext>();
        services.AddScoped<Jobly.Core.Handlers.IJobContext>(x => x.GetRequiredService<Jobly.Core.Handlers.JobContext>());
        services.AddJoblyRetry(o =>
        {
            o.MaxRetries = maxRetries;
            o.Delays = delays ?? [];
        });

        var workerConfig = new OptionsWrapper<JoblyWorkerConfiguration>(new JoblyWorkerConfiguration
        {
            WorkerCount = 1,
            ServerId = ServerId,
            Queues = DefaultQueues,
        });
        services.AddSingleton<IOptions<JoblyWorkerConfiguration>>(workerConfig);
        services.AddSingleton<IOptions<JoblyConfiguration>>(workerConfig);
        services.TryAddSingleton(TimeProvider.System);

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
    public async Task GetAndProcessJob_WithMaxRetries3_RetriesThreeTimesThenFails()
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

        var worker = CreateWorker(maxRetries: 3);

        // Act — process 4 times (1 initial + 3 retries)
        for (var i = 0; i < 4; i++)
        {
            await worker.GetAndProcessJob(CancellationToken.None);
        }

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Failed);
        GetRetriedTimes(job).ShouldBe(3);
    }

    [Fact]
    public async Task GetAndProcessJob_WithMaxRetries0_FailsImmediately()
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

        var worker = CreateWorker(maxRetries: 0);

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Failed);
        GetRetriedTimes(job).ShouldBe(0);
    }

    [Fact]
    public async Task GetAndProcessJob_RetryDoesNotIncrementFailedStat()
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

        var worker = CreateWorker(maxRetries: 2);

        // Act — process once (should be requeued, not failed)
        await worker.GetAndProcessJob(CancellationToken.None);

        await CounterAggregatorTask<TestContext>.AggregateCounters(_fixture.CreateContext());

        // Assert — stats:failed should NOT be incremented during retry
        var failedStat = await _fixture.CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        failedStat.ShouldBe(0);

        // Verify job was requeued (not failed)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
        GetRetriedTimes(job).ShouldBe(1);
    }

    [Fact]
    public async Task GetAndProcessJob_RetryThenSucceed_CompletesNormally()
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

        var worker = CreateWorker(maxRetries: 3);

        // Act — first attempt fails and gets requeued
        await worker.GetAndProcessJob(CancellationToken.None);

        // Change job type to succeed on next attempt
        var updateCtx = _fixture.CreateContext();
        await updateCtx.Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.Type, typeof(UnitRequest).AssemblyQualifiedName)
                .SetProperty(p => p.Message, JsonSerializer.Serialize(new UnitRequest()))
                .SetProperty(p => p.HandlerType, (string?)null));

        // Process again — should succeed
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Completed);
        GetRetriedTimes(job).ShouldBe(1);
    }

    [Fact]
    public async Task GetAndProcessJob_WithRetryDelays_SetsScheduleTimeInFuture()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ThrowExceptionRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ThrowExceptionRequest()),
            CreateTime = now,
            ScheduleTime = now,
            Queue = "default",

        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(maxRetries: 1, delays: [3600]);

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert — job should be requeued with future ScheduleTime
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
        job.ScheduleTime.ShouldBeGreaterThan(now.AddSeconds(3500));
    }

    [Fact]
    public async Task GetAndProcessJob_WithRetryDelays_LastDelayReusedWhenArrayShorter()
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

        var worker = CreateWorker(maxRetries: 3, delays: [10, 20]);

        // Act — process 3 times (1 initial + 2 retries)
        for (var i = 0; i < 3; i++)
        {
            // Reset ScheduleTime so the worker can pick up the job again
            var resetCtx = _fixture.CreateContext();
            await resetCtx.Set<Job>()
                .Where(x => x.Id == jobId)
                .Where(x => x.CurrentState == State.Enqueued)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.ScheduleTime, DateTime.UtcNow));

            await worker.GetAndProcessJob(CancellationToken.None);
        }

        // Assert — 3rd retry should use last delay (20s)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        GetRetriedTimes(job).ShouldBe(3);
        job.ScheduleTime.ShouldBeGreaterThan(DateTime.UtcNow.AddSeconds(15));
    }

    [Fact]
    public async Task GetAndProcessJob_WithEmptyRetryDelays_RetriesImmediately()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        var originalScheduleTime = DateTime.UtcNow;
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ThrowExceptionRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ThrowExceptionRequest()),
            CreateTime = originalScheduleTime,
            ScheduleTime = originalScheduleTime,
            Queue = "default",

        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(maxRetries: 1, delays: []);

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert — ScheduleTime should still be in the past (no delay applied)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
        job.ScheduleTime.ShouldBeLessThanOrEqualTo(TimeProvider.System.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task GetAndProcessJob_WithRetryDelays_JobNotPickedUpBeforeDelay()
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

        var worker = CreateWorker(maxRetries: 1, delays: [3600]);

        // Act — first call processes and requeues with 1h delay
        await worker.GetAndProcessJob(CancellationToken.None);

        // Second call should not find the job (ScheduleTime is in the future)
        var didProcess = await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        didProcess.ShouldBeFalse();
    }

    [Fact]
    public async Task GetAndProcessJob_WithPerJobRetryDelays_OverridesGlobalConfig()
    {
        // Arrange — job has per-job $retryDelays in metadata
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var metadata = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["RetryDelays"] = new int[] { 7200 },
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(ThrowExceptionRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new ThrowExceptionRequest()),
            CreateTime = now,
            ScheduleTime = now,
            Queue = "default",

            Metadata = metadata,
        });
        await ctx.SaveChangesAsync();

        // Global config has 60s delay, but per-job has 7200s
        var worker = CreateWorker(maxRetries: 1, delays: [60]);

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert — should use per-job delay (7200s), not global (60s)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.ScheduleTime.ShouldBeGreaterThan(now.AddSeconds(7000));
    }

    [Fact]
    public async Task GetAndProcessJob_WithMaxRetriesInMetadata_UsesMetadataValue()
    {
        // Arrange — job has per-job $maxRetries in metadata
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        var metadata = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["MaxRetries"] = 2,
        });
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

            Metadata = metadata,
        });
        await ctx.SaveChangesAsync();

        // Global config has 0 retries, but per-job metadata has 2
        var worker = CreateWorker(maxRetries: 0);

        // Act — process 3 times (1 initial + 2 retries from metadata)
        for (var i = 0; i < 3; i++)
        {
            await worker.GetAndProcessJob(CancellationToken.None);
        }

        // Assert — should have retried 2 times (from metadata), then failed
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Failed);
        GetRetriedTimes(job).ShouldBe(2);
    }

    [Fact]
    public async Task GetAndProcessJob_WithRetryAttributeOnHandler_UsesAttributeMaxRetries()
    {
        // Arrange — handler has [Retry(5)], global has 0
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(RetryAttributeHandlerRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new RetryAttributeHandlerRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(maxRetries: 0);

        // Act — process once, should be requeued (attribute says 5 retries)
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
        GetRetriedTimes(job).ShouldBe(1);
    }

    [Fact]
    public async Task GetAndProcessJob_WithRetryAttributeOnJob_UsesAttributeMaxRetries()
    {
        // Arrange — job class has [Retry(4)], global has 0
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(RetryAttributeJobRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new RetryAttributeJobRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(maxRetries: 0);

        // Act — process once, should be requeued (attribute says 4 retries)
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
        GetRetriedTimes(job).ShouldBe(1);
    }

    [Fact]
    public async Task GetAndProcessJob_WithRetryAttributeOnBothHandlerAndJob_HandlerWins()
    {
        // Arrange — handler has [Retry(7)], job has [Retry(2)], global has 0
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(RetryAttributeBothRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new RetryAttributeBothRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(maxRetries: 0);

        // Act — exhaust all retries from handler attribute (7)
        for (var i = 0; i < 8; i++)
        {
            var resetCtx = _fixture.CreateContext();
            await resetCtx.Set<Job>()
                .Where(x => x.Id == jobId)
                .Where(x => x.CurrentState == State.Enqueued)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.ScheduleTime, DateTime.UtcNow));

            await worker.GetAndProcessJob(CancellationToken.None);
        }

        // Assert — should have used handler's 7 retries (not job's 2)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Failed);
        GetRetriedTimes(job).ShouldBe(7);
    }

    [Fact]
    public async Task GetAndProcessJob_MetadataOverridesRetryAttribute()
    {
        // Arrange — handler has [Retry(5)], but metadata overrides to 1
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        var metadata = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["MaxRetries"] = 1,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(RetryAttributeHandlerRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new RetryAttributeHandlerRequest()),
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            Metadata = metadata,
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(maxRetries: 0);

        // Act — process twice (1 initial + 1 retry from metadata)
        for (var i = 0; i < 2; i++)
        {
            await worker.GetAndProcessJob(CancellationToken.None);
        }

        // Assert — should have used metadata's 1 retry (not handler attribute's 5)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Failed);
        GetRetriedTimes(job).ShouldBe(1);
    }

    [Fact]
    public async Task GetAndProcessJob_WithRetryAttributeDelays_UsesAttributeDelays()
    {
        // Arrange — handler has [Retry(3, Delays = [100, 200, 300])]
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Type = typeof(RetryAttributeWithDelaysRequest).AssemblyQualifiedName,
            Message = JsonSerializer.Serialize(new RetryAttributeWithDelaysRequest()),
            CreateTime = now,
            ScheduleTime = now,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Global has 10s delay, attribute has 100s
        var worker = CreateWorker(maxRetries: 0, delays: [10]);

        // Act
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert — should use attribute's 100s delay (not global 10s)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
        job.ScheduleTime.ShouldBeGreaterThan(now.AddSeconds(90));
    }
}

[Collection<PostgreSqlCollection>]
public class RetryTests_PostgreSql : RetryTestsBase
{
    public RetryTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class RetryTests_SqlServer : RetryTestsBase
{
    public RetryTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
