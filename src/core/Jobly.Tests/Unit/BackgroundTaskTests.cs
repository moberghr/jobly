using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

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
        await ctx.SaveChangesAsync();

        // Act
        var aggCtx = _fixture.CreateContext();
        await CounterAggregatorTask<TestContext>.AggregateCounters(aggCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var succeeded = await readCtx.Set<Statistic>().FindAsync("stats:succeeded");
        succeeded.ShouldNotBeNull();
        succeeded.Value.ShouldBe(8);

        var failed = await readCtx.Set<Statistic>().FindAsync("stats:failed");
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
        await ctx.SaveChangesAsync();

        // Act
        var aggCtx = _fixture.CreateContext();
        await CounterAggregatorTask<TestContext>.AggregateCounters(aggCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var counters = await readCtx.Set<Counter>().ToListAsync();
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
        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
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
        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
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
        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
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
        await ctx.SaveChangesAsync();

        // Act
        var recoveryCtx = _fixture.CreateContext();
        var count = await StaleJobRecoveryTask<TestContext>.RequeueStaleJobs(recoveryCtx, TimeProvider.System, TimeSpan.FromMinutes(5));

        // Assert
        count.ShouldBe(1);
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
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
        await ctx.SaveChangesAsync();

        // Act
        var recoveryCtx = _fixture.CreateContext();
        var count = await StaleJobRecoveryTask<TestContext>.RequeueStaleJobs(recoveryCtx, TimeProvider.System, TimeSpan.FromMinutes(5));

        // Assert
        count.ShouldBe(0);
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId);
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
        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        var count = await ServerCleanupTask<TestContext>.CleanUpServers(cleanCtx, TimeProvider.System, TimeSpan.FromMinutes(5));

        // Assert
        count.ShouldBe(1);
        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>().FirstOrDefaultAsync(s => s.Id == serverId);
        server.ShouldBeNull();
    }
}

[Collection<PostgreSqlCollection>]
public class BackgroundTaskTests_PostgreSql : BackgroundTaskTestsBase
{
    public BackgroundTaskTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class BackgroundTaskTests_SqlServer : BackgroundTaskTestsBase
{
    public BackgroundTaskTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
