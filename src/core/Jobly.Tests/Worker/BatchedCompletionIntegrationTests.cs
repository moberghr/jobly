using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Worker;

[GenerateDatabaseTests(FixtureKind.BatchedCompletion)]
public abstract class BatchedCompletionIntegrationTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected BatchedCompletionIntegrationTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    protected JoblyTestServer Server => _fixture.TestServer!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _fixture.ResetAsync();
        }
        catch
        {
            await Task.Delay(100);
            await _fixture.ResetAsync();
        }

        await Server.ReRegisterServer();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact(timeout: 60_000)]
    public async Task GivenManyShortJobs_WhenProcessed_ThenAllReachCompletedState()
    {
        // Arrange
        var publisher = Server.CreatePublisher();
        const int jobCount = 200;
        for (var i = 0; i < jobCount; i++)
        {
            await publisher.Enqueue(new UnitRequest());
        }

        await publisher.SaveChangesAsync();

        // Act
        await Server.WaitForCompletion(TimeSpan.FromSeconds(45));

        // Assert — all jobs reach Completed, and one Completed JobLog exists per job.
        // Per-row terminal-state fields must also match non-dispatcher mode: no worker
        // ownership left, no keep-alive, no lingering cancellation flag, ExpireAt set.
        var ctx = Server.CreateContext();
        var completedCount = await ctx.Set<Job>()
            .Where(j => j.Kind == JobKind.Job && j.CurrentState == State.Completed)
            .CountAsync();
        completedCount.ShouldBe(jobCount);

        var jobs = await ctx.Set<Job>()
            .Where(j => j.Kind == JobKind.Job)
            .AsNoTracking()
            .ToListAsync();
        jobs.ShouldAllBe(j => j.CurrentWorkerId == null);
        jobs.ShouldAllBe(j => j.LastKeepAlive == null);
        jobs.ShouldAllBe(j => j.CancellationMode == CancellationMode.None);
        jobs.ShouldAllBe(j => j.ExpireAt != null);

        var completedLogs = await ctx.Set<JobLog>()
            .Where(l => l.EventType == "Completed")
            .CountAsync();
        completedLogs.ShouldBe(jobCount);
    }

    [TimedFact(timeout: 60_000)]
    public async Task GivenMixedOutcomes_WhenProcessed_ThenStatesMatch()
    {
        // Arrange
        var publisher = Server.CreatePublisher();
        var successType = typeof(UnitRequest).AssemblyQualifiedName;
        var failType = typeof(ThrowExceptionRequest).AssemblyQualifiedName;

        const int successCount = 25;
        const int failCount = 15;
        for (var i = 0; i < successCount; i++)
        {
            await publisher.Enqueue(new UnitRequest());
        }

        for (var i = 0; i < failCount; i++)
        {
            await publisher.Enqueue(new ThrowExceptionRequest());
        }

        await publisher.SaveChangesAsync();

        // Act
        await Server.WaitForCompletion(TimeSpan.FromSeconds(45));

        // Assert
        var ctx = Server.CreateContext();
        var completed = await ctx.Set<Job>()
            .CountAsync(j => j.Type == successType && j.CurrentState == State.Completed);
        completed.ShouldBe(successCount);

        var failed = await ctx.Set<Job>()
            .CountAsync(j => j.Type == failType && j.CurrentState == State.Failed);
        failed.ShouldBe(failCount);
    }

    [TimedFact(timeout: 60_000)]
    public async Task GivenIdleWorker_WhenFewJobsBelowBatchSize_ThenBufferIsDrainedByIdleTrigger()
    {
        // Arrange — fixture's CompletionBatchSize is 10; publishing 3 jobs never hits the size threshold.
        // Idle drain should flush them before workers suspend on the channel.
        var publisher = Server.CreatePublisher();
        for (var i = 0; i < 3; i++)
        {
            await publisher.Enqueue(new UnitRequest());
        }

        await publisher.SaveChangesAsync();

        // Act
        await Server.WaitForCompletion(TimeSpan.FromSeconds(15));

        // Assert
        var ctx = Server.CreateContext();
        var completed = await ctx.Set<Job>()
            .CountAsync(j => j.Kind == JobKind.Job && j.CurrentState == State.Completed);
        completed.ShouldBe(3);
    }

    [TimedFact(timeout: 60_000)]
    public async Task GivenShutdown_WhenServerStops_ThenPendingCompletionsAreFlushed()
    {
        // Arrange — spin up a test-local server on an isolated queue so the fixture server doesn't race us.
        // A long flush interval ensures completions stay buffered until StopAsync drains them.
        const string isolatedQueue = "batch-shutdown-flush";
        var server = await JoblyTestServer.StartAsync(_fixture, config =>
        {
            config.UseDispatcher = true;
            config.WorkerCount = 2;
            config.Queues = [isolatedQueue];
            config.CompletionBatchSize = 50;
            config.CompletionFlushInterval = TimeSpan.FromSeconds(30);
        });

        try
        {
            var publisher = server.CreatePublisher();
            for (var i = 0; i < 5; i++)
            {
                await publisher.Enqueue(new UnitRequest(), queue: isolatedQueue);
            }

            await publisher.SaveChangesAsync();

            // Wait for handlers to run (empty handlers complete ~instantly).
            // Completions are buffered; the long flush interval means they won't auto-flush.
            await server.WaitForJobsToLeaveEnqueued(isolatedQueue, 5, TimeSpan.FromSeconds(10));
        }
        finally
        {
            // StopAsync override should drain the buffered completions.
            await server.DisposeAsync();
        }

        // Assert — after shutdown, all jobs must be persisted as Completed.
        var ctx = _fixture.CreateContext();
        var jobs = await ctx.Set<Job>()
            .Where(j => j.Queue == isolatedQueue)
            .AsNoTracking()
            .ToListAsync();
        jobs.Count.ShouldBe(5);
        jobs.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [TimedFact(timeout: 60_000)]
    public async Task GivenProcessingJob_WhenCancelledInDispatcherMode_ThenFlushesAsDeletedWithHourlyCounter()
    {
        // Arrange — fixture server runs in dispatcher mode. Enqueue a handler that blocks on cancellation.
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Processing);

        // Act — cancel while in-flight; worker's handler token is cancelled, the catch path builds a
        // PendingCompletion with State.Deleted and the batch flushes.
        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        await Server.WaitForJobState(jobId, State.Deleted, TimeSpan.FromSeconds(15));

        // Assert — terminal state + Cancelled log.
        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
        job.CancellationMode.ShouldBe(CancellationMode.None);
        job.CurrentWorkerId.ShouldBeNull();
        job.ExpireAt.ShouldNotBeNull();

        var logs = await Server.GetJobLogs(jobId);
        logs.ShouldContain(l => l.EventType == "Cancelled");

        // W2 — both the aggregate and hourly counters must be written, matching non-dispatcher mode.
        var ctx = Server.CreateContext();
        var hourSuffix = DateTime.UtcNow.ToString("yyyy-MM-dd-HH", System.Globalization.CultureInfo.InvariantCulture);
        var aggregateSum = await ctx.Set<Counter>()
            .Where(c => c.Key == "stats:deleted")
            .SumAsync(c => c.Value);
        aggregateSum.ShouldBeGreaterThanOrEqualTo(1);

        var hourlySum = await ctx.Set<Counter>()
            .Where(c => c.Key == $"stats:deleted:{hourSuffix}")
            .SumAsync(c => c.Value);
        hourlySum.ShouldBeGreaterThanOrEqualTo(1);
    }

    [TimedFact(timeout: 60_000)]
    public async Task GivenCompletionBatchSizeOne_WhenProcessed_ThenEachJobCommitsIndividually()
    {
        // Arrange — isolated server with batching disabled (opt-out path).
        const string isolatedQueue = "batch-size-one";
        await using var server = await JoblyTestServer.StartAsync(_fixture, config =>
        {
            config.UseDispatcher = true;
            config.WorkerCount = 2;
            config.Queues = [isolatedQueue];
            config.CompletionBatchSize = 1;
            config.CompletionFlushInterval = TimeSpan.FromSeconds(30);
        });

        var publisher = server.CreatePublisher();
        for (var i = 0; i < 8; i++)
        {
            await publisher.Enqueue(new UnitRequest(), queue: isolatedQueue);
        }

        await publisher.SaveChangesAsync();

        // Act
        await server.WaitForCompletion(TimeSpan.FromSeconds(30));

        // Assert
        var ctx = _fixture.CreateContext();
        var jobs = await ctx.Set<Job>()
            .Where(j => j.Queue == isolatedQueue)
            .CountAsync(j => j.CurrentState == State.Completed);
        jobs.ShouldBe(8);
    }

}
