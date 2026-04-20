using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.Helpers;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Reliability;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class CrashRecoveryTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected CrashRecoveryTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task RequeueStaleJobs_MultipleStaleJobs_AllRequeued()
    {
        // Arrange — insert 5 stale Processing jobs
        var ctx = _fixture.CreateContext();
        var jobIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var jobId = Guid.NewGuid();
            jobIds.Add(jobId);
            ctx.Set<Job>().Add(new Job
            {
                Id = jobId,
                Kind = JobKind.Job,
                CurrentState = State.Processing,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                LastKeepAlive = DateTime.UtcNow.AddMinutes(-10),
            });
        }

        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var result = await TestTasks
            .CreateStaleJobRecoveryTask(_fixture.CreateContext(), TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(_fixture.CreateContext(), CancellationToken.None);

        // Assert
        result.Requeued.ShouldBe(5);
        var readCtx = _fixture.CreateContext();
        foreach (var id in jobIds)
        {
            var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == id, Xunit.TestContext.Current.CancellationToken);
            job.CurrentState.ShouldBe(State.Enqueued);
        }
    }

    [TimedFact]
    public async Task RequeueStaleJobs_NonProcessingJobs_NotAffected()
    {
        // Arrange — insert jobs in Completed, Failed, and Enqueued states with old keepalive
        var ctx = _fixture.CreateContext();
        var staleTime = DateTime.UtcNow.AddMinutes(-10);

        var completedId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = completedId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = staleTime,
        });

        var failedId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = failedId,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = staleTime,
        });

        var enqueuedId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = enqueuedId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = staleTime,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var result = await TestTasks
            .CreateStaleJobRecoveryTask(_fixture.CreateContext(), TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(_fixture.CreateContext(), CancellationToken.None);

        // Assert
        result.Total.ShouldBe(0);

        var readCtx = _fixture.CreateContext();
        (await readCtx.Set<Job>().FirstAsync(j => j.Id == completedId, Xunit.TestContext.Current.CancellationToken)).CurrentState.ShouldBe(State.Completed);
        (await readCtx.Set<Job>().FirstAsync(j => j.Id == failedId, Xunit.TestContext.Current.CancellationToken)).CurrentState.ShouldBe(State.Failed);
        (await readCtx.Set<Job>().FirstAsync(j => j.Id == enqueuedId, Xunit.TestContext.Current.CancellationToken)).CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task RequeueStaleJobs_StaleJob_RetriedTimesNotIncremented()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = DateTime.UtcNow.AddMinutes(-10),
            RetriedTimes = 2,
            MaxRetries = 5,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        await TestTasks
            .CreateStaleJobRecoveryTask(_fixture.CreateContext(), TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(_fixture.CreateContext(), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.CurrentState.ShouldBe(State.Enqueued);
        job.RetriedTimes.ShouldBe(2); // Unchanged
    }

    [TimedFact]
    public async Task RequeueStaleJobs_ConcurrentCalls_OnlyOnceRequeued()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = DateTime.UtcNow.AddMinutes(-10),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act — run 5 concurrent requeue attempts
        var tasks = Enumerable.Range(0, 5)
            .Select(_ =>
            {
                var ctx = _fixture.CreateContext();
                return TestTasks.CreateStaleJobRecoveryTask(ctx, TimeProvider.System, TimeSpan.FromMinutes(5))
                    .RecoverStaleJobsAsync(ctx, CancellationToken.None);
            })
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert — exactly 1 should have requeued the job
        results.Sum(x => x.Requeued).ShouldBe(1);

        var logs = await _fixture.CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Requeued")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        logs.Count.ShouldBe(1);
    }

    [TimedFact]
    public async Task CleanUpServers_DeadServerWithProcessingJob_JobStateUnchanged()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();

        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow.AddHours(-2),
            LastHeartbeatTime = DateTime.UtcNow.AddMinutes(-10),
            ServiceCount = 1,
        });
        ctx.Set<Jobly.Core.Data.Entities.Worker>().Add(new Jobly.Core.Data.Entities.Worker
        {
            Id = workerId,
            ServerId = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow.AddMinutes(-10),
        });

        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            CurrentWorkerId = workerId,
            LastKeepAlive = DateTime.UtcNow.AddMinutes(-10),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act — cleanup only removes server/workers, not jobs
        await TestTasks
            .CreateServerCleanupTask(_fixture.CreateContext(), TimeProvider.System, TimeSpan.FromMinutes(5))
            .CleanUpServersAsync(_fixture.CreateContext(), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.CurrentState.ShouldBe(State.Processing); // Unchanged — StaleJobRecovery handles this
    }

    [TimedFact]
    public async Task CleanUpServers_CombinedRecovery_JobsRequeuedAndServerCleaned()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();

        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow.AddHours(-2),
            LastHeartbeatTime = DateTime.UtcNow.AddMinutes(-10),
            ServiceCount = 1,
        });
        ctx.Set<Jobly.Core.Data.Entities.Worker>().Add(new Jobly.Core.Data.Entities.Worker
        {
            Id = workerId,
            ServerId = serverId,
            StartedTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow.AddMinutes(-10),
        });

        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            CurrentWorkerId = workerId,
            LastKeepAlive = DateTime.UtcNow.AddMinutes(-10),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act — run both cleanup + recovery (as health manager would)
        var recovery = await TestTasks
            .CreateStaleJobRecoveryTask(_fixture.CreateContext(), TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(_fixture.CreateContext(), CancellationToken.None);
        var removed = await TestTasks
            .CreateServerCleanupTask(_fixture.CreateContext(), TimeProvider.System, TimeSpan.FromMinutes(5))
            .CleanUpServersAsync(_fixture.CreateContext(), CancellationToken.None);

        // Assert
        recovery.Requeued.ShouldBe(1);
        removed.ShouldBe(1);

        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.CurrentState.ShouldBe(State.Enqueued);

        var servers = await readCtx.Set<Server>().CountAsync(Xunit.TestContext.Current.CancellationToken);
        servers.ShouldBe(0);
    }

    [TimedFact]
    public async Task RequeueStaleJobs_KeepAliveAtExactCutoff_NotRequeued()
    {
        // Arrange — job with LastKeepAlive exactly at cutoff should NOT be requeued (strict < comparison)
        var ctx = _fixture.CreateContext();
        var timeout = TimeSpan.FromMinutes(5);
        var now = DateTime.UtcNow.AddMinutes(10);
        var exactCutoff = now - timeout;

        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = exactCutoff,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var tp = new FakeTimeProvider(now);
        var result = await TestTasks
            .CreateStaleJobRecoveryTask(_fixture.CreateContext(), tp, timeout)
            .RecoverStaleJobsAsync(_fixture.CreateContext(), CancellationToken.None);

        // Assert — should NOT be requeued (at boundary, not past it)
        result.Total.ShouldBe(0);
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync([jobId], Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Processing);
    }

    [TimedFact]
    public async Task CleanUpServers_HeartbeatAtExactTimeout_NotCleaned()
    {
        // Arrange — server with heartbeat exactly at timeout boundary should NOT be cleaned.
        // Round `now` to microsecond precision so LastHeartbeatTime survives PostgreSQL round-trip
        // (timestamp has 6-digit precision; raw .NET ticks have 7 digits). Without this, the saved
        // value would be truncated and `now - savedHeartbeat` would exceed timeout by sub-microsecond
        // ticks, falsely tripping the `>` boundary check.
        var ctx = _fixture.CreateContext();
        var timeout = TimeSpan.FromMinutes(5);
        var rawNow = DateTime.UtcNow.AddMinutes(10);
        var now = new DateTime(rawNow.Ticks - (rawNow.Ticks % 10), DateTimeKind.Utc);

        var serverId = Guid.NewGuid();
        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = now.AddHours(-1),
            LastHeartbeatTime = now - timeout,
            ServiceCount = 1,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var tp = new FakeTimeProvider(now);
        await TestTasks
            .CreateServerCleanupTask(_fixture.CreateContext(), tp, timeout)
            .CleanUpServersAsync(_fixture.CreateContext(), CancellationToken.None);

        // Assert — server should still exist
        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>().FindAsync([serverId], Xunit.TestContext.Current.CancellationToken);
        server.ShouldNotBeNull();
    }

    /// <summary>
    /// CRITICAL #1: Stale recovery must respect CancellationMode.
    /// If a job has CancellationMode=Graceful (user called DeleteJob), stale recovery
    /// should set it to Deleted, NOT requeue it.
    /// </summary>
    [TimedFact]
    public async Task StaleRecovery_WithCancellationModeGraceful_SetsDeletedNotEnqueued()
    {
        // Arrange: a processing job with CancellationMode=Graceful and stale keep-alive
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CancellationMode = CancellationMode.Graceful,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = DateTime.UtcNow.AddMinutes(-10), // stale
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act: run stale recovery
        var recoveryCtx = _fixture.CreateContext();
        await TestTasks
            .CreateStaleJobRecoveryTask(recoveryCtx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(recoveryCtx, CancellationToken.None);

        // Assert: job should be Deleted (not Enqueued) because user intended to cancel it
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync([jobId], Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Deleted, "Stale job with CancellationMode=Graceful should be Deleted, not requeued");
        job.CancellationMode.ShouldBe(CancellationMode.None);
    }
}

file class FakeTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
