using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class MutexTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected MutexTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly string[] DefaultQueues = ["default"];

    [Fact]
    public async Task MutexHeld_SecondJobCancelled()
    {
        // Arrange: two jobs with same concurrency key, first already processing
        var ctx = _fixture.CreateContext();
        var job1Id = Guid.NewGuid();
        var job2Id = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = job1Id,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddMinutes(-1),
            Queue = "default",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = "{}",
            ConcurrencyKey = "payment:123",
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = job2Id,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddMinutes(-1),
            Queue = "default",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = "{}",
            ConcurrencyKey = "payment:123",
        });
        await ctx.SaveChangesAsync();

        // Pre-hold the lock (simulating job1 is being processed by another worker)
        var lockProvider = new FakeLockProvider();
        var heldLock = lockProvider.CreateLock("jobly:mutex:payment:123");
        var heldHandle = await heldLock.AcquireAsync();

        // Act: worker processes job2 — should fail to acquire lock
        var worker = CreateWorker(lockProvider);
        await worker.GetAndProcessJob(CancellationToken.None);

        // Cleanup
        await heldHandle.DisposeAsync();

        // Assert: job2 should be cancelled (Deleted)
        var readCtx = _fixture.CreateContext();
        var job2 = await readCtx.Set<Job>().FindAsync([job2Id], CancellationToken.None);
        job2.ShouldNotBeNull();
        job2.CurrentState.ShouldBe(State.Deleted);

        var log = await readCtx.Set<JobLog>()
            .FirstOrDefaultAsync(l => l.JobId == job2Id && l.EventType == "Deleted", CancellationToken.None);
        log.ShouldNotBeNull();
        log.Message.ShouldContain("mutex");
        log.Message.ShouldContain("payment:123");
    }

    [Fact]
    public async Task DifferentMutexKeys_BothProcess()
    {
        // Arrange: two jobs with different concurrency keys
        var ctx = _fixture.CreateContext();
        ctx.Set<Job>().Add(new Job
        {
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddMinutes(-1),
            Queue = "default",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = "{}",
            ConcurrencyKey = "payment:123",
        });
        var job2Id = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = job2Id,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddMinutes(-1),
            Queue = "default",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = "{}",
            ConcurrencyKey = "payment:456",
        });
        await ctx.SaveChangesAsync();

        // Act
        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert: job2 should be processing (not cancelled)
        var readCtx = _fixture.CreateContext();
        var job2 = await readCtx.Set<Job>().FindAsync(job2Id);
        job2.ShouldNotBeNull();
        job2.CurrentState.ShouldNotBe(State.Deleted);
    }

    [Fact]
    public async Task NoConcurrencyKey_NoMutexCheck()
    {
        // Arrange: job without concurrency key
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddMinutes(-1),
            Queue = "default",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = "{}",
        });
        await ctx.SaveChangesAsync();

        // Act
        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert: job should complete normally (no mutex cancellation)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task MutexFree_JobProcessesNormally()
    {
        // Arrange: job with concurrency key but no other job holds it
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddMinutes(-1),
            Queue = "default",
            Type = typeof(UnitRequest).AssemblyQualifiedName,
            Message = "{}",
            ConcurrencyKey = "payment:123",
        });
        await ctx.SaveChangesAsync();

        // Act
        var worker = CreateWorker();
        await worker.GetAndProcessJob(CancellationToken.None);

        // Assert: should complete
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Completed);
    }

    private JoblyWorkerService<TestContext> CreateWorker(IDistributedLockProvider? lockProvider = null)
    {
        lockProvider ??= new FakeLockProvider();
        var services = new ServiceCollection();
        services.AddHandlers(typeof(MutexTestsBase).Assembly);
        services.AddLogging();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());
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
            lockProvider);
    }
}

[Collection("PostgreSql")]
public class MutexTests_PostgreSql : MutexTestsBase
{
    public MutexTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class MutexTests_SqlServer : MutexTestsBase
{
    public MutexTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
