using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Helper;
using Warp.Core.Mutex;
using Warp.Core.Retry;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;
using Warp.Worker.Services;

namespace Warp.Tests.EndToEnd;

[GenerateDatabaseTests(FixtureKind.MultiServer)]
public abstract class MultiServerTestsBase : MultiServerIntegrationTestBase
{
    protected MultiServerTestsBase(IMultiServerDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenManyJobs_WithTwoServers_ThenEachJobProcessedExactlyOnce()
    {
        var publisher = Server1.CreatePublisher();
        var jobIds = new List<Guid>();
        for (var i = 0; i < 50; i++)
        {
            jobIds.Add(await publisher.Enqueue(new CounterRequest()));
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server1.WaitForCompletion();

        var ctx = CreateContext();

        // All 50 jobs should be completed
        var completedCount = await ctx.Set<Job>()
            .Where(x => jobIds.Contains(x.Id))
            .Where(x => x.CurrentState == State.Completed)
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        completedCount.ShouldBe(50);

        // No stuck jobs
        var activeCount = await ctx.Set<Job>()
            .Where(x => jobIds.Contains(x.Id))
            .Where(x => x.CurrentState == State.Enqueued
                || x.CurrentState == State.Processing
                || x.CurrentState == State.Awaiting)
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        activeCount.ShouldBe(0);

        // Each job processed exactly once — one Processing log and one Completed log per job
        foreach (var jobId in jobIds)
        {
            var processingLogs = await ctx.Set<JobLog>()
                .Where(x => x.JobId == jobId)
                .Where(x => x.EventType == "Processing")
                .CountAsync(Xunit.TestContext.Current.CancellationToken);
            processingLogs.ShouldBe(1, $"Job {jobId} should have exactly one Processing log");

            var completedLogs = await ctx.Set<JobLog>()
                .Where(x => x.JobId == jobId)
                .Where(x => x.EventType == "Completed")
                .CountAsync(Xunit.TestContext.Current.CancellationToken);
            completedLogs.ShouldBe(1, $"Job {jobId} should have exactly one Completed log");
        }
    }

    [TimedFact]
    public async Task GivenMessages_WithTwoServers_ThenEachRoutedExactlyOnce()
    {
        var publisher = Server1.CreatePublisher();
        var messageIds = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            messageIds.Add(await publisher.Publish(new SingleHandlerMessage()));
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server1.WaitForCompletion();

        var ctx = CreateContext();

        // Each message should be completed
        foreach (var messageId in messageIds)
        {
            var message = await ctx.Set<Job>()
                .Where(x => x.Id == messageId)
                .FirstAsync(Xunit.TestContext.Current.CancellationToken);
            message.CurrentState.ShouldBe(State.Completed);
            message.Kind.ShouldBe(JobKind.Message);

            // Each message should have exactly 1 child job (SingleHandlerMessage has 1 handler)
            // If message routing ran twice, there would be 2 children
            var childCount = await ctx.Set<Job>()
                .Where(x => x.ParentJobId == messageId)
                .Where(x => x.Kind == JobKind.Job)
                .CountAsync(Xunit.TestContext.Current.CancellationToken);
            childCount.ShouldBe(1, $"Message {messageId} should have exactly 1 child job (not double-routed)");
        }
    }

    [TimedFact]
    public async Task GivenMultiHandlerMessage_WithTwoServers_ThenCorrectChildCount()
    {
        var publisher = Server1.CreatePublisher();
        var messageIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            messageIds.Add(await publisher.Publish(new MultiRequest()));
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server1.WaitForCompletion();

        var ctx = CreateContext();

        foreach (var messageId in messageIds)
        {
            var message = await ctx.Set<Job>()
                .Where(x => x.Id == messageId)
                .FirstAsync(Xunit.TestContext.Current.CancellationToken);
            message.CurrentState.ShouldBe(State.Completed);

            // MultiRequest has 2 handlers (MultiHandlerA + MultiHandlerB)
            // If routing ran twice, there would be 4 children
            var childCount = await ctx.Set<Job>()
                .Where(x => x.ParentJobId == messageId)
                .Where(x => x.Kind == JobKind.Job)
                .CountAsync(Xunit.TestContext.Current.CancellationToken);
            childCount.ShouldBe(2, $"Message {messageId} should have exactly 2 children (not double-routed)");
        }
    }

    // Multi-server batch + continuation (20+3 jobs across 2 servers) — waits on distributed
    // orchestration with real runtime ~5–10s under CI contention.
    [TimedFact(45_000)]
    public async Task GivenBatch_WithTwoServers_ThenBatchCompletesCorrectly()
    {
        var batchPublisher = Server1.CreateBatchPublisher();

        var batchJobs = Enumerable.Range(0, 20)
            .Select(_ => new UnitRequest())
            .ToList();
        var batchId = await batchPublisher.StartNew(batchJobs);

        var continuationJobs = Enumerable.Range(0, 3)
            .Select(_ => new UnitRequest())
            .ToList();
        await batchPublisher.ContinueBatchWith(continuationJobs, batchId);
        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server1.WaitForCompletion(timeout: TimeSpan.FromSeconds(30));

        var ctx = CreateContext();

        // Batch parent should be completed
        var batch = await ctx.Set<Job>()
            .Where(x => x.Id == batchId)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);
        batch.CurrentState.ShouldBe(State.Completed);
        batch.Kind.ShouldBe(JobKind.Batch);

        // 20 direct batch children should be completed
        var batchChildren = await ctx.Set<Job>()
            .Where(x => x.ParentJobId == batchId)
            .Where(x => x.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        batchChildren.Count.ShouldBe(20);
        batchChildren.ShouldAllBe(x => x.CurrentState == State.Completed);

        // Continuation batch (Kind=Batch, ParentId=batchId) should be completed
        var continuationBatch = await ctx.Set<Job>()
            .Where(x => x.ParentJobId == batchId)
            .Where(x => x.Kind == JobKind.Batch)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);
        continuationBatch.CurrentState.ShouldBe(State.Completed);

        // Continuation batch's 3 children should be completed
        var continuationChildren = await ctx.Set<Job>()
            .Where(x => x.ParentJobId == continuationBatch.Id)
            .Where(x => x.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        continuationChildren.Count.ShouldBe(3);
        continuationChildren.ShouldAllBe(x => x.CurrentState == State.Completed);
    }

    [TimedFact]
    public async Task GivenContinuations_WithTwoServers_ThenContinuationsActivateAndExecute()
    {
        var publisher = Server1.CreatePublisher();
        var parentIds = new List<Guid>();

        // Create 5 parent → child continuation chains
        for (var i = 0; i < 5; i++)
        {
            var parentId = await publisher.Enqueue(new UnitRequest());
            await publisher.Enqueue(new UnitRequest(), parentId);
            parentIds.Add(parentId);
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server1.WaitForCompletion();

        var ctx = CreateContext();

        foreach (var parentId in parentIds)
        {
            // Parent should be completed
            var parent = await ctx.Set<Job>()
                .Where(x => x.Id == parentId)
                .FirstAsync(Xunit.TestContext.Current.CancellationToken);
            parent.CurrentState.ShouldBe(State.Completed);

            // Continuation child should be completed
            var children = await ctx.Set<Job>()
                .Where(x => x.ParentJobId == parentId)
                .ToListAsync(Xunit.TestContext.Current.CancellationToken);
            children.Count.ShouldBe(1);
            children[0].CurrentState.ShouldBe(State.Completed);
        }
    }

    [TimedFact]
    public async Task GivenMutexJobs_WithTwoServers_ThenMutexEnforcedAcrossServers()
    {
        // Enqueue a slow job that holds the mutex — published via Server1
        var publisher1 = Server1.CreatePublisher();
        var job1Id = await publisher1.Enqueue(new CancellableRequest(), new JobParameters().WithMutex("multi-server-mutex"));
        await publisher1.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait for it to start processing
        await Server1.WaitForJobState(job1Id, State.Processing);

        // Enqueue a second job with the same mutex — published via Server2
        var publisher2 = Server2.CreatePublisher();
        var job2Id = await publisher2.Enqueue(new UnitRequest(), new JobParameters().WithMutex("multi-server-mutex"));
        await publisher2.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Job2 should be deleted due to mutex (regardless of which server picks it up).
        await Server1.WaitForJobState(job2Id, State.Deleted);

        // Verify the mutex violation was logged
        var logs = await Server1.GetJobLogs(job2Id);
        logs.ShouldContain(x => x.EventType == "Deleted" && x.Message.Contains("mutex"));

        // Job1 should still be processing
        var job1 = await Server1.GetJob(job1Id);
        job1.CurrentState.ShouldBe(State.Processing);

        // Cancel the slow job and wait for it to be deleted so it doesn't leak into subsequent tests
        var cmd = Server1.CreateCommandService();
        await cmd.DeleteJob(job1Id);
        await Server1.WaitForJobState(job1Id, State.Deleted, timeout: TimeSpan.FromSeconds(5));
    }

    // Multi-server complex workload (50+ jobs: simple + messages + failing + retries +
    // batch + continuations on 2 servers) — real runtime ~10–25s under CI contention.
    [TimedFact(90_000)]
    public async Task GivenComplexWorkload_WithTwoServers_ThenAllReachTerminalState()
    {
        var publisher = Server1.CreatePublisher();
        var batchPublisher = Server1.CreateBatchPublisher();

        // 1. Simple jobs (30)
        for (var i = 0; i < 30; i++)
        {
            await publisher.Enqueue(new UnitRequest());
        }

        // 2. Messages with single handler (5)
        for (var i = 0; i < 5; i++)
        {
            await publisher.Publish(new SingleHandlerMessage());
        }

        // 3. Messages with multiple handlers (5 → 10 handler jobs)
        for (var i = 0; i < 5; i++)
        {
            await publisher.Publish(new MultiRequest());
        }

        // 4. Failing jobs (5)
        for (var i = 0; i < 5; i++)
        {
            await publisher.Enqueue(new ThrowExceptionRequest());
        }

        // 5. Failing jobs with retries (3, maxRetries=2)
        for (var i = 0; i < 3; i++)
        {
            await publisher.Enqueue(new ThrowExceptionRequest(), new JobParameters().Configure<IRetryMetadata>(m => m.MaxRetries = 2));
        }

        // 6. Batch of 10 → continuation of 3
        var batchJobs = Enumerable.Range(0, 10)
            .Select(_ => new UnitRequest())
            .ToList();
        var batchId = await batchPublisher.StartNew(batchJobs);
        var continuationJobs = Enumerable.Range(0, 3)
            .Select(_ => new UnitRequest())
            .ToList();
        await batchPublisher.ContinueBatchWith(continuationJobs, batchId);
        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // 7. Parent → child continuations (5 chains)
        for (var i = 0; i < 5; i++)
        {
            var parentId = await publisher.Enqueue(new UnitRequest());
            await publisher.Enqueue(new UnitRequest(), parentId);
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server1.WaitForCompletion(timeout: TimeSpan.FromSeconds(60));

        var ctx = CreateContext();

        // No stuck jobs
        var stuckJobs = await ctx.Set<Job>()
            .Where(x => x.Kind == JobKind.Job)
            .Where(x => x.CurrentState == State.Enqueued
                || x.CurrentState == State.Scheduled
                || x.CurrentState == State.Processing
                || x.CurrentState == State.Awaiting)
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        stuckJobs.ShouldBe(0, "No jobs should be stuck in non-terminal states");

        // All messages completed
        var incompleteMessages = await ctx.Set<Job>()
            .Where(x => x.Kind == JobKind.Message)
            .Where(x => x.CurrentState != State.Completed)
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        incompleteMessages.ShouldBe(0, "All messages should be completed");

        // All batches completed
        var incompleteBatches = await ctx.Set<Job>()
            .Where(x => x.Kind == JobKind.Batch)
            .Where(x => x.CurrentState != State.Completed)
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        incompleteBatches.ShouldBe(0, "All batches should be completed");

        // Failed count: 5 no-retry + 3 with-retry = 8 failed
        var failedJobs = await ctx.Set<Job>()
            .Where(x => x.Kind == JobKind.Job)
            .Where(x => x.CurrentState == State.Failed)
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        failedJobs.ShouldBe(8, "5 no-retry + 3 with-retry = 8 failed");

        // Completed count should be > 0
        var completedJobs = await ctx.Set<Job>()
            .Where(x => x.Kind == JobKind.Job)
            .Where(x => x.CurrentState == State.Completed)
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        completedJobs.ShouldBeGreaterThan(50, "Should have many completed jobs");

        // No terminal jobs should have a CurrentWorkerId
        var jobsWithWorker = await ctx.Set<Job>()
            .Where(x => x.Kind == JobKind.Job)
            .Where(x => x.CurrentWorkerId != null)
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        jobsWithWorker.ShouldBe(0, "No terminal jobs should have a CurrentWorkerId");

        // Stats counts are already verified via the completed and failed Job.CurrentState
        // totals above. Asserting on the aggregated rows would require forcing the lock
        // protected server-side aggregator from the test, which is not a supported path.
        // Dedicated aggregator unit tests cover the counter-to-stat rollup in isolation.
    }

    [TimedFact]
    public async Task GivenPausedServer_WithTwoServers_ThenOtherServerProcessesJobs()
    {
        // Get Server1's worker group and pause it
        var ctx = CreateContext();
        var server1GroupId = await ctx.Set<WorkerGroup>()
            .Where(x => x.ServerId == Server1.ServerId)
            .Select(x => x.Id)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);

        var svc = Server1.CreateServerCommandService();
        await svc.PauseServer(Server1.ServerId);
        await Server1.WaitForPauseState(server1GroupId, expectedPaused: true);

        // Enqueue jobs while Server1 is paused
        var publisher = Server2.CreatePublisher();
        var jobIds = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            jobIds.Add(await publisher.Enqueue(new UnitRequest()));
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Jobs should be processed by Server2
        await Server2.WaitForCompletion();

        ctx = CreateContext();

        // All jobs completed
        var completedCount = await ctx.Set<Job>()
            .Where(x => jobIds.Contains(x.Id))
            .Where(x => x.CurrentState == State.Completed)
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        completedCount.ShouldBe(10);

        // All jobs should have been processed by Server2's workers (Server1 is paused)
        var server2WorkerIds = await ctx.Set<Warp.Core.Data.Entities.Worker>()
            .Where(x => x.ServerId == Server2.ServerId)
            .Select(x => x.Id)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        var processingWorkerIds = await ctx.Set<JobLog>()
            .Where(x => jobIds.Contains(x.JobId))
            .Where(x => x.EventType == "Processing")
            .Where(x => x.WorkerId != null)
            .Select(x => x.WorkerId!.Value)
            .Distinct()
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        processingWorkerIds.ShouldAllBe(
            x => server2WorkerIds.Contains(x),
            "All jobs should have been processed by Server2's workers while Server1 was paused");

        // Resume Server1
        await svc.ResumeServer(Server1.ServerId);
        await Server1.WaitForPauseState(server1GroupId, expectedPaused: false);
    }
}
