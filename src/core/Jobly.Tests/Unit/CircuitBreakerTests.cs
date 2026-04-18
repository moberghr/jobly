using System.Text.Json;
using Jobly.Core;
using Jobly.Core.CircuitBreaker;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Mutex;
using Jobly.Core.Retry;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class CircuitBreakerTestsBase : IAsyncLifetime
{
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly string[] DefaultQueues = ["default"];

    private readonly IDatabaseFixture _fixture;

    protected CircuitBreakerTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task GroupBelowThreshold_Increments()
    {
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = nameof(ThrowExceptionRequest),
            FailureCount = 1,
            LastFailureAt = now.AddSeconds(-30),
        });
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(ThrowExceptionRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == nameof(ThrowExceptionRequest))
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBe(2);
        state.OpenUntil.ShouldBeNull();
    }

    [TimedFact]
    public async Task FailureHitsThreshold_OpensCircuit()
    {
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = nameof(ThrowExceptionRequest),
            FailureCount = 2,
            LastFailureAt = now.AddSeconds(-30),
        });
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(ThrowExceptionRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        var duration = TimeSpan.FromMinutes(1);
        var worker = CreateWorker(duration: duration);

        var before = DateTime.UtcNow;
        await worker.GetAndProcessJob(CancellationToken.None);
        var after = DateTime.UtcNow;

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == nameof(ThrowExceptionRequest))
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBe(3);
        state.OpenUntil.ShouldNotBeNull();
        state.OpenUntil!.Value.ShouldBeGreaterThanOrEqualTo(before + duration - TimeSpan.FromSeconds(5));
        state.OpenUntil.Value.ShouldBeLessThanOrEqualTo(after + duration + TimeSpan.FromSeconds(5));
    }

    [TimedFact]
    public async Task OpenCircuit_ReschedulesJob()
    {
        var now = DateTime.UtcNow;
        var openUntil = now.AddMinutes(5);
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = nameof(UnitRequest),
            FailureCount = 3,
            OpenUntil = openUntil,
            LastFailureAt = now.AddSeconds(-30),
        });
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        var jitter = TimeSpan.FromSeconds(10);
        var worker = CreateWorker(resetJitter: jitter);
        await worker.GetAndProcessJob(CancellationToken.None);

        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
        job.ScheduleTime.ShouldBeGreaterThanOrEqualTo(openUntil);
        job.ScheduleTime.ShouldBeLessThanOrEqualTo(openUntil + jitter);

        var log = await readCtx.Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .Where(x => x.Message.Contains("circuit breaker"))
            .FirstOrDefaultAsync(CancellationToken.None);
        log.ShouldNotBeNull();
        log.Message.ShouldContain(nameof(UnitRequest));
    }

    [TimedFact]
    public async Task Success_ResetsCounter()
    {
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = nameof(UnitRequest),
            FailureCount = 2,
            LastFailureAt = now.AddSeconds(-30),
        });
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == nameof(UnitRequest))
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBe(0);
        state.OpenUntil.ShouldBeNull();
    }

    [TimedFact]
    public async Task OutcomeAlreadyEnqueued_SkipsCount()
    {
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = nameof(ThrowExceptionRequest),
            FailureCount = 1,
            LastFailureAt = now.AddSeconds(-30),
        });
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(ThrowExceptionRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker(retryMaxRetries: 5);
        await worker.GetAndProcessJob(CancellationToken.None);

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == nameof(ThrowExceptionRequest))
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBe(1);
    }

    [TimedFact]
    public async Task AttributeGroupOverride_UsesCustomKey()
    {
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(CircuitBreakerGroupRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == "email-service")
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBe(1);

        var byTypeName = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == nameof(CircuitBreakerGroupRequest))
            .FirstOrDefaultAsync(CancellationToken.None);
        byTypeName.ShouldBeNull();
    }

    [TimedFact]
    public async Task FirstFailure_CreatesRowLazily()
    {
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(ThrowExceptionRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == nameof(ThrowExceptionRequest))
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBe(1);
        state.OpenUntil.ShouldBeNull();
    }

    [TimedFact]
    public async Task RescheduleTime_WithinJitterWindow()
    {
        var now = DateTime.UtcNow;
        var openUntil = now.AddMinutes(5);
        var jitter = TimeSpan.FromSeconds(1);

        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = nameof(UnitRequest),
            FailureCount = 3,
            OpenUntil = openUntil,
            LastFailureAt = now.AddSeconds(-30),
        });

        var jobIds = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            var jobId = Guid.NewGuid();
            jobIds.Add(jobId);
            ctx.Set<Job>().Add(new Job
            {
                Id = jobId,
                Kind = JobKind.Job,
                CurrentState = State.Enqueued,
                CreateTime = now,
                ScheduleTime = now.AddMinutes(-1),
                Queue = "default",
                Type = typeof(UnitRequest).AssemblyQualifiedName,
                Message = "{}",
            });
        }

        await ctx.SaveChangesAsync();

        var worker = CreateWorker(resetJitter: jitter);
        for (var i = 0; i < 20; i++)
        {
            await worker.GetAndProcessJob(CancellationToken.None);
        }

        var readCtx = _fixture.CreateContext();
        var jobs = await readCtx.Set<Job>()
            .Where(x => jobIds.Contains(x.Id))
            .ToListAsync(CancellationToken.None);
        jobs.Count.ShouldBe(20);
        foreach (var job in jobs)
        {
            job.CurrentState.ShouldBe(State.Enqueued);
            job.ScheduleTime.ShouldBeGreaterThanOrEqualTo(openUntil);
            job.ScheduleTime.ShouldBeLessThanOrEqualTo(openUntil + jitter);
        }
    }

    [TimedFact]
    public async Task MutexDeletedOutcome_SkipsCountAndReset()
    {
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = nameof(MutexAttributeRequest),
            FailureCount = 2,
            LastFailureAt = now.AddSeconds(-30),
        });
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(MutexAttributeRequest).AssemblyQualifiedName,
            Message = "{}",
            Metadata = JsonSerializer.Serialize(new Dictionary<string, object> { ["ConcurrencyKey"] = "static-key" }),
        });
        await ctx.SaveChangesAsync();

        var lockProvider = new FakeLockProvider();
        var heldHandle = lockProvider.HoldLock("jobly:mutex:static-key");

        var worker = CreateWorker(lockProvider: lockProvider);
        await worker.GetAndProcessJob(CancellationToken.None);
        await heldHandle.DisposeAsync();

        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Deleted);

        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == nameof(MutexAttributeRequest))
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBe(2);
        state.OpenUntil.ShouldBeNull();
    }

    // Probabilistic: this test exercises the DbUpdateException fallback path by firing 10
    // concurrent RecordFailureAsync calls with no pre-existing row. At least one task is
    // overwhelmingly likely to hit the PK-collision branch, but this can't be enforced
    // deterministically without test-only hooks inside the store. The final-count invariant
    // (== 10) holds regardless of whether the race fired or calls fully serialized — so the
    // fallback branch's correctness is validated in practice, not strictly guaranteed by
    // assertion. If this becomes flaky under heavy parallelism, move to a hook-based test.
    [TimedFact]
    public async Task RecordFailure_ConcurrentFirstFailures_AllCounted()
    {
        var key = "race-" + Guid.NewGuid().ToString("N");
        var scopeFactory = CreateStoreScopeFactory();
        var now = DateTime.UtcNow;

        var tasks = Enumerable.Range(0, 10).Select(_ =>
        {
            var store = new CircuitBreakerStore<TestContext>(_fixture.CreateContext(), scopeFactory);

            return store.RecordFailureAsync(key, threshold: 100, duration: TimeSpan.FromMinutes(1), now, CancellationToken.None);
        }).ToArray();

        await Task.WhenAll(tasks);

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == key)
            .FirstOrDefaultAsync(CancellationToken.None);
        state.ShouldNotBeNull();
        state.FailureCount.ShouldBe(10);
        state.OpenUntil.ShouldBeNull();
    }

    [TimedFact]
    public async Task ExpiredOpenCircuit_WhenProbeSucceeds_ThenCircuitClosesAndResets()
    {
        // End-to-end coverage for the probe-success path. Pre-fix to PR #126 review F2,
        // this path went through the behavior with no probe gate (thundering herd); the
        // introduced CAS must also integrate with the existing success-reset logic so the
        // winning probe worker actually transitions the state row from HalfOpen → Closed
        // and zeroes the counter. Snapshot-vs-DB skew is tolerated: the pipeline's local
        // `state` snapshot was Open before probe, so the success-reset heuristic fires.
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = nameof(UnitRequest),
            FailureCount = 3,
            LastFailureAt = now.AddMinutes(-5),
            OpenUntil = now.AddSeconds(-1),
            State = CircuitState.Open,
        });
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Completed);

        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == nameof(UnitRequest))
            .FirstAsync(CancellationToken.None);
        state.State.ShouldBe(CircuitState.Closed);
        state.FailureCount.ShouldBe(0);
        state.OpenUntil.ShouldBeNull();
    }

    [TimedFact]
    public async Task ExpiredOpenCircuit_WhenProbeFails_ThenCircuitReopensWithFreshOpenUntil()
    {
        // End-to-end coverage for the probe-failure path: a failing probe must transition
        // HalfOpen → Open with a fresh OpenUntil; otherwise a flaky downstream would
        // continuously be probed with no cooldown. RecordFailureAsync's HalfOpen branch
        // must trip regardless of FailureCount vs threshold.
        var now = DateTime.UtcNow;
        var duration = TimeSpan.FromMinutes(1);
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = nameof(ThrowExceptionRequest),
            FailureCount = 3,
            LastFailureAt = now.AddMinutes(-5),
            OpenUntil = now.AddSeconds(-1),
            State = CircuitState.Open,
        });
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
            Queue = "default",
            Type = typeof(ThrowExceptionRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        var before = DateTime.UtcNow;
        var worker = CreateWorker(duration: duration);
        await worker.GetAndProcessJob(CancellationToken.None);
        var after = DateTime.UtcNow;

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == nameof(ThrowExceptionRequest))
            .FirstAsync(CancellationToken.None);
        state.State.ShouldBe(CircuitState.Open);
        state.FailureCount.ShouldBe(4);
        state.OpenUntil.ShouldNotBeNull();
        state.OpenUntil!.Value.ShouldBeGreaterThanOrEqualTo(before + duration - TimeSpan.FromSeconds(5));
        state.OpenUntil.Value.ShouldBeLessThanOrEqualTo(after + duration + TimeSpan.FromSeconds(5));
    }

    [TimedFact]
    public async Task TryBeginProbeAsync_ConcurrentCallsAfterExpiry_OnlyOneWins()
    {
        // Regression for PR #126 review F2: when OpenUntil lapses, all workers polling
        // simultaneously observe an expired circuit and all execute the handler concurrently —
        // the exact thundering herd a circuit breaker is meant to prevent. The fix is a
        // half-open probe gate: exactly one worker atomically transitions Open → HalfOpen and
        // probes the downstream; the others observe HalfOpen and reschedule.
        var key = "probe-race-" + Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = key,
            FailureCount = 3,
            LastFailureAt = now.AddSeconds(-120),
            OpenUntil = now.AddSeconds(-1),
            State = CircuitState.Open,
        });
        await ctx.SaveChangesAsync();

        var scopeFactory = CreateStoreScopeFactory();
        var tasks = Enumerable.Range(0, 10).Select(_ =>
        {
            var store = new CircuitBreakerStore<TestContext>(_fixture.CreateContext(), scopeFactory);

            return store.TryBeginProbeAsync(key, now, CancellationToken.None);
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        results.Count(x => x).ShouldBe(1);

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == key)
            .FirstAsync(CancellationToken.None);
        state.State.ShouldBe(CircuitState.HalfOpen);
    }

    [TimedFact]
    public async Task TryBeginProbeAsync_WhenOpenUntilNotElapsed_ReturnsFalse()
    {
        var key = "probe-early-" + Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = key,
            FailureCount = 3,
            LastFailureAt = now,
            OpenUntil = now.AddMinutes(1),
            State = CircuitState.Open,
        });
        await ctx.SaveChangesAsync();

        var scopeFactory = CreateStoreScopeFactory();
        var store = new CircuitBreakerStore<TestContext>(_fixture.CreateContext(), scopeFactory);

        var won = await store.TryBeginProbeAsync(key, now, CancellationToken.None);

        won.ShouldBeFalse();

        var readCtx = _fixture.CreateContext();
        var state = await readCtx.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == key)
            .FirstAsync(CancellationToken.None);
        state.State.ShouldBe(CircuitState.Open);
    }

    [TimedFact]
    public async Task TryBeginProbeAsync_WhenAlreadyHalfOpen_ReturnsFalse()
    {
        var key = "probe-duplicate-" + Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var ctx = _fixture.CreateContext();
        ctx.Set<CircuitBreakerState>().Add(new CircuitBreakerState
        {
            GroupKey = key,
            FailureCount = 3,
            LastFailureAt = now.AddSeconds(-120),
            OpenUntil = now.AddSeconds(-1),
            State = CircuitState.HalfOpen,
        });
        await ctx.SaveChangesAsync();

        var scopeFactory = CreateStoreScopeFactory();
        var store = new CircuitBreakerStore<TestContext>(_fixture.CreateContext(), scopeFactory);

        var won = await store.TryBeginProbeAsync(key, now, CancellationToken.None);

        won.ShouldBeFalse();
    }

    private IServiceScopeFactory CreateStoreScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private JoblyWorkerService<TestContext> CreateWorker(
        TimeSpan? duration = null,
        TimeSpan? resetJitter = null,
        int? retryMaxRetries = null,
        FakeLockProvider? lockProvider = null)
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(CircuitBreakerTestsBase).Assembly);
        services.AddLogging();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());
        services.AddScoped<JobContext>();
        services.AddScoped<IJobContext>(x => x.GetRequiredService<JobContext>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddJoblyCircuitBreaker<TestContext>(o =>
        {
            if (duration != null)
            {
                o.Duration = duration.Value;
            }

            if (resetJitter != null)
            {
                o.ResetJitter = resetJitter.Value;
            }
        });

        if (retryMaxRetries != null)
        {
            services.AddJoblyRetry(o => o.MaxRetries = retryMaxRetries.Value);
        }

        if (lockProvider != null)
        {
            services.AddSingleton<IJoblyLockProvider>(lockProvider);
            services.AddJoblyMutex();
        }

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
            TimeProvider.System);
    }
}

[Collection<PostgreSqlCollection>]
public class CircuitBreakerTests_PostgreSql : CircuitBreakerTestsBase
{
    public CircuitBreakerTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class CircuitBreakerTests_SqlServer : CircuitBreakerTestsBase
{
    public CircuitBreakerTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
