using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.Helpers;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Orchestration;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class BackgroundTaskTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected BackgroundTaskTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // --- CounterAggregatorTask ---
    [TimedFact]
    public async Task AggregateCounters_SumsCountersIntoStatistics()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        ctx.Set<Counter>().Add(new Counter { Key = "stats:succeeded", Value = 3 });
        ctx.Set<Counter>().Add(new Counter { Key = "stats:succeeded", Value = 5 });
        ctx.Set<Counter>().Add(new Counter { Key = "stats:failed", Value = 2 });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var aggCtx = _fixture.CreateContext();
        await CounterAggregatorTask<TestContext>.AggregateCounters(aggCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var succeeded = await readCtx.Set<Statistic>().FindAsync(["stats:succeeded"], Xunit.TestContext.Current.CancellationToken);
        succeeded.ShouldNotBeNull();
        succeeded.Value.ShouldBe(8);

        var failed = await readCtx.Set<Statistic>().FindAsync(["stats:failed"], Xunit.TestContext.Current.CancellationToken);
        failed.ShouldNotBeNull();
        failed.Value.ShouldBe(2);
    }

    [TimedFact]
    public async Task AggregateCounters_DeletesProcessedCounters()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        ctx.Set<Counter>().Add(new Counter { Key = "stats:succeeded", Value = 1 });
        ctx.Set<Counter>().Add(new Counter { Key = "stats:failed", Value = 1 });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var aggCtx = _fixture.CreateContext();
        await CounterAggregatorTask<TestContext>.AggregateCounters(aggCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var counters = await readCtx.Set<Counter>().ToListAsync(Xunit.TestContext.Current.CancellationToken);
        counters.Count.ShouldBe(0);
    }

    // --- ExpirationCleanupTask ---
    [TimedFact]
    public async Task ExpirationCleanup_DeletesExpiredJobs()
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
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldBeNull();
    }

    [TimedFact]
    public async Task ExpirationCleanup_KeepsNonExpiredJobs()
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
            ExpireAt = DateTime.UtcNow.AddHours(2),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task ExpirationCleanup_KeepsFailedJobsWithoutExpireAt()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ExpireAt = null,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
    }

    // --- StaleJobRecoveryTask ---
    [TimedFact]
    public async Task StaleJobRecovery_RequeuesStaleJobs()
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

        // Act
        var recoveryCtx = _fixture.CreateContext();
        var result = await TestTasks
            .CreateStaleJobRecoveryTask(recoveryCtx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(recoveryCtx, Xunit.TestContext.Current.CancellationToken);

        // Assert
        result.Requeued.ShouldBe(1);
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task StaleJobRecovery_KeepsFreshJobs()
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
            LastKeepAlive = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var recoveryCtx = _fixture.CreateContext();
        var result = await TestTasks
            .CreateStaleJobRecoveryTask(recoveryCtx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(recoveryCtx, Xunit.TestContext.Current.CancellationToken);

        // Assert
        result.Total.ShouldBe(0);
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Processing);
    }

    // --- ServerCleanupTask ---
    [TimedFact]
    public async Task ServerCleanup_RemovesDeadServers()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = DateTime.UtcNow.AddHours(-2),
            LastHeartbeatTime = DateTime.UtcNow.AddMinutes(-10),
            ServiceCount = 1,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var cleanCtx = _fixture.CreateContext();
        var count = await TestTasks
            .CreateServerCleanupTask(cleanCtx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .CleanUpServersAsync(cleanCtx, Xunit.TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);
        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>().FirstOrDefaultAsync(s => s.Id == serverId, Xunit.TestContext.Current.CancellationToken);
        server.ShouldBeNull();
    }

    [TimedFact]
    public async Task AggregateCounters_NoCounters_ReturnsZero()
    {
        // Arrange — empty counters table
        var aggCtx = _fixture.CreateContext();

        // Act
        var count = await CounterAggregatorTask<TestContext>.AggregateCounters(aggCtx);

        // Assert
        count.ShouldBe(0);
    }

    [TimedFact]
    public async Task AggregateCounters_ExistingStat_IncrementsValue()
    {
        // Arrange — pre-existing stat + new counter for the same key
        var ctx = _fixture.CreateContext();
        ctx.Set<Statistic>().Add(new Statistic { Key = "stats:completed", Value = 100 });
        ctx.Set<Counter>().Add(new Counter { Key = "stats:completed", Value = 5 });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var aggCtx = _fixture.CreateContext();
        await CounterAggregatorTask<TestContext>.AggregateCounters(aggCtx);

        // Assert — should increment existing stat, not create a new one
        var readCtx = _fixture.CreateContext();
        var stat = await readCtx.Set<Statistic>().FindAsync(["stats:completed"], Xunit.TestContext.Current.CancellationToken);
        stat.ShouldNotBeNull();
        stat.Value.ShouldBe(105);

        var statCount = await readCtx.Set<Statistic>().CountAsync(x => x.Key == "stats:completed", Xunit.TestContext.Current.CancellationToken);
        statCount.ShouldBe(1);
    }

    [TimedFact]
    public async Task AggregateCounters_NewKey_CreatesStatistic()
    {
        // Arrange — counter for a key that has no existing stat
        var ctx = _fixture.CreateContext();
        ctx.Set<Counter>().Add(new Counter { Key = "stats:new-key", Value = 3 });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var aggCtx = _fixture.CreateContext();
        await CounterAggregatorTask<TestContext>.AggregateCounters(aggCtx);

        // Assert — should create new stat with correct value
        var readCtx = _fixture.CreateContext();
        var stat = await readCtx.Set<Statistic>().FindAsync(["stats:new-key"], Xunit.TestContext.Current.CancellationToken);
        stat.ShouldNotBeNull();
        stat.Value.ShouldBe(3);
    }
}
