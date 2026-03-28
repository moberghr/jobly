using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    // ==================== Core Requeue Behavior ====================

    [Fact]
    public async Task GivenStaleProcessingJob_WhenRequeueStaleJobsRuns_ThenJobIsRequeued()
    {
        await EnsureServerRegistered();
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        // Simulate worker picked up the job then crashed
        await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6)));

        var requeued = await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));

        requeued.ShouldBe(1);
        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
        job.CurrentWorkerId.ShouldBeNull();
        job.LastKeepAlive.ShouldBeNull();

        var logs = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Requeued")
            .ToListAsync();
        logs.Count.ShouldBe(1);
        logs[0].Message.ShouldContain("crash recovery");
    }

    [Fact]
    public async Task GivenFreshProcessingJob_WhenRequeueStaleJobsRuns_ThenJobIsNotRequeued()
    {
        await EnsureServerRegistered();
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        // Simulate worker actively processing (fresh keep-alive)
        await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow));

        var requeued = await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));

        requeued.ShouldBe(0);
        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Processing);
        job.CurrentWorkerId.ShouldBe(TestUtils.TestWorkerId);
    }

    [Fact]
    public async Task GivenStaleJob_WhenRequeued_ThenRetriedTimesIsNotIncremented()
    {
        await EnsureServerRegistered();
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context, retries: 5);
        var jobId = await publisher.Enqueue(new ThrowExceptionRequest(), maxRetries: 5);
        await context.SaveChangesAsync();

        // Simulate job had been retried twice before crash
        await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6))
                .SetProperty(p => p.RetriedTimes, 2));

        await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
        job.RetriedTimes.ShouldBe(2); // Unchanged
    }

    [Fact]
    public async Task GivenMultipleStaleJobs_WhenRequeueStaleJobsRuns_ThenAllAreRequeued()
    {
        await EnsureServerRegistered();

        var jobIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var ctx = CreateContext();
            var pub = TestUtils.CreatePublisher(ctx);
            var id = await pub.Enqueue(new UnitRequest());
            await ctx.SaveChangesAsync();
            jobIds.Add(id);
        }

        // Simulate all 3 crashed
        foreach (var id in jobIds)
        {
            await CreateContext().Set<Job>()
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(p => p.CurrentState, State.Processing)
                    .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                    .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6)));
        }

        var requeued = await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));

        requeued.ShouldBe(3);
        foreach (var id in jobIds)
        {
            var job = await GetJob(id);
            job.CurrentState.ShouldBe(State.Enqueued);
        }
    }

    [Fact]
    public async Task GivenNonProcessingJobs_WhenRequeueStaleJobsRuns_ThenNoneAreAffected()
    {
        await EnsureServerRegistered();

        // Create jobs in various non-Processing states with stale LastKeepAlive
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var enqueuedId = await publisher.Enqueue(new UnitRequest());
        var completedId = await publisher.Enqueue(new UnitRequest());
        var failedId = await publisher.Enqueue(new ThrowExceptionRequest());
        await context.SaveChangesAsync();

        // Process completed and failed jobs
        await ProcessJob(); // completes UnitRequest
        await ProcessJob(); // completes UnitRequest
        await ProcessJob(); // fails ThrowExceptionRequest

        // Set stale LastKeepAlive on all (shouldn't matter since they're not Processing)
        var staleTime = DateTime.UtcNow.AddMinutes(-10);
        foreach (var id in new[] { enqueuedId, completedId, failedId })
        {
            await CreateContext().Set<Job>()
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastKeepAlive, staleTime));
        }

        var requeued = await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));
        requeued.ShouldBe(0);
    }

    // ==================== Full Lifecycle ====================

    [Fact]
    public async Task GivenCrashedJob_WhenRequeuedAndProcessed_ThenJobCompletes()
    {
        await EnsureServerRegistered();
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        // Simulate crash
        await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6)));

        await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));

        // Now process normally
        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);

        // Should have both Requeued and Completed log entries
        var logs = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .ToListAsync();
        logs.ShouldContain(l => l.EventType == "Requeued");
        logs.ShouldContain(l => l.EventType == "Completed");
    }

    [Fact]
    public async Task GivenJobWithRetries_WhenCrashedThenFailsNormally_ThenCrashDoesNotCountAgainstRetries()
    {
        await EnsureServerRegistered();
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new ThrowExceptionRequest(), maxRetries: 1);
        await context.SaveChangesAsync();

        // Simulate crash
        await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6)));

        await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));

        var job = await GetJob(jobId);
        job.RetriedTimes.ShouldBe(0); // Crash didn't count

        // First real failure: retried (RetriedTimes 0 < MaxRetries 1)
        await ProcessJob();
        job = await GetJob(jobId);
        job.RetriedTimes.ShouldBe(1);
        job.CurrentState.ShouldBe(State.Enqueued); // Still has retries left

        // Second real failure: exhausted (RetriedTimes 1 < MaxRetries 1 is false)
        await ProcessJob();
        job = await GetJob(jobId);
        job.RetriedTimes.ShouldBe(1); // Doesn't increment past max
        job.CurrentState.ShouldBe(State.Failed);
    }

    // ==================== Server Cleanup ====================

    [Fact]
    public async Task GivenDeadServer_WhenCleanUpServersRuns_ThenServerAndWorkersAreRemoved()
    {
        var context = CreateContext();
        await TestUtils.RegisterTestServer(context, workerCount: 3);

        // Set server heartbeat to stale
        await CreateContext().Set<Server>()
            .Where(x => x.Id == TestUtils.TestServerId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastHeartbeatTime, DateTime.UtcNow.AddMinutes(-6)));

        var removed = await JoblyHealthManager<TestContext>.CleanUpServers(CreateContext(), TimeSpan.FromMinutes(5));

        removed.ShouldBe(1);
        var servers = await CreateContext().Set<Server>().ToListAsync();
        servers.Count.ShouldBe(0);
        var workers = await CreateContext().Set<Jobly.Core.Data.Entities.Worker>().ToListAsync();
        workers.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GivenDeadServerWithProcessingJob_WhenCleanUpServersRuns_ThenJobStateIsUnchanged()
    {
        var context = CreateContext();
        await TestUtils.RegisterTestServer(context);

        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        // Simulate job picked up by crashed worker
        await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6)));

        // Set server heartbeat to stale
        await CreateContext().Set<Server>()
            .Where(x => x.Id == TestUtils.TestServerId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastHeartbeatTime, DateTime.UtcNow.AddMinutes(-6)));

        // CleanUpServers only removes server/workers, not jobs
        await JoblyHealthManager<TestContext>.CleanUpServers(CreateContext(), TimeSpan.FromMinutes(5));

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Processing); // Not failed — RequeueStaleJobs handles this
    }

    [Fact]
    public async Task GivenDeadServer_WhenBothRecoveryMethodsRun_ThenJobsRequeuedAndServerCleaned()
    {
        var context = CreateContext();
        await TestUtils.RegisterTestServer(context);

        var publisher = TestUtils.CreatePublisher(context);
        var jobId1 = await publisher.Enqueue(new UnitRequest());
        var jobId2 = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        // Simulate both jobs picked up by crashed workers
        foreach (var id in new[] { jobId1, jobId2 })
        {
            await CreateContext().Set<Job>()
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(p => p.CurrentState, State.Processing)
                    .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                    .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6)));
        }

        // Set server heartbeat to stale
        await CreateContext().Set<Server>()
            .Where(x => x.Id == TestUtils.TestServerId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastHeartbeatTime, DateTime.UtcNow.AddMinutes(-6)));

        // Run both recovery methods (as health manager would)
        var requeued = await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));
        var removed = await JoblyHealthManager<TestContext>.CleanUpServers(CreateContext(), TimeSpan.FromMinutes(5));

        requeued.ShouldBe(2);
        removed.ShouldBe(1);

        (await GetJob(jobId1)).CurrentState.ShouldBe(State.Enqueued);
        (await GetJob(jobId2)).CurrentState.ShouldBe(State.Enqueued);
        (await CreateContext().Set<Server>().CountAsync()).ShouldBe(0);
    }

    // ==================== Keep-Alive During Execution ====================

    [Fact]
    public async Task GivenJob_WhenProcessed_ThenLastKeepAliveIsClearedAfterCompletion()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
        job.LastKeepAlive.ShouldBeNull();
    }

    [Fact]
    public async Task GivenFailingJob_WhenProcessed_ThenLastKeepAliveIsClearedAfterFailure()
    {
        var context = CreateContext();
        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Failed);
        job.LastKeepAlive.ShouldBeNull();
    }

    // ==================== Concurrency ====================

    [Fact]
    public async Task GivenStaleJob_WhenMultipleRequeueStaleJobsRunConcurrently_ThenOnlyOnceRequeued()
    {
        await EnsureServerRegistered();
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6)));

        // Run 5 concurrent requeue attempts
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5)))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Exactly 1 should have requeued the job
        results.Sum().ShouldBe(1);

        var logs = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Requeued")
            .ToListAsync();
        logs.Count.ShouldBe(1);
    }

    // ==================== Edge Cases ====================

    [Fact]
    public async Task GivenBatchJobCrash_WhenRequeued_ThenBatchCounterUnaffectedAndBatchCompletes()
    {
        await EnsureServerRegistered();
        var context = CreateContext();
        var batchId = await CreateBatch(context, 3);
        await context.SaveChangesAsync();

        // Get one of the batch jobs
        var batchJobs = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == batchId)
            .ToListAsync();
        batchJobs.Count.ShouldBe(3);

        var crashedJobId = batchJobs[0].Id;

        // Simulate crash on first job
        await CreateContext().Set<Job>()
            .Where(x => x.Id == crashedJobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6)));

        // Check batch counter before requeue
        var batchBefore = await CreateContext().Set<Batch>()
            .Where(x => x.Id == batchId)
            .FirstAsync();
        var counterBefore = batchBefore.Counter;

        await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));

        // Batch counter unchanged by crash requeue
        var batchAfter = await CreateContext().Set<Batch>()
            .Where(x => x.Id == batchId)
            .FirstAsync();
        batchAfter.Counter.ShouldBe(counterBefore);

        // Process all jobs (including the requeued one) — batch should complete
        await ProcessAllJobs();

        var batchFinal = await CreateContext().Set<Batch>()
            .Where(x => x.Id == batchId)
            .FirstAsync();
        batchFinal.Counter.ShouldBe(0);
    }

    [Fact]
    public async Task GivenMessageSpawnedJobCrash_WhenRequeued_ThenMessageStaysProcessing()
    {
        await EnsureServerRegistered();
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // Route message (creates 2 jobs) + execute first
        await ProcessJob();

        var message = await GetMessage(messageId);
        message.CurrentState.ShouldBe(State.Processing);
        var jobCountBefore = message.JobCount;

        // Get the remaining enqueued job and simulate crash on it
        var jobs = await GetJobsForMessage(messageId);
        var enqueuedJob = jobs.First(j => j.CurrentState == State.Enqueued);

        await CreateContext().Set<Job>()
            .Where(x => x.Id == enqueuedJob.Id)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.CurrentWorkerId, TestUtils.TestWorkerId)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-6)));

        await JoblyHealthManager<TestContext>.RequeueStaleJobs(CreateContext(), TimeSpan.FromMinutes(5));

        // Message should still be Processing with same JobCount (crash requeue doesn't touch messages)
        var messageAfter = await GetMessage(messageId);
        messageAfter.CurrentState.ShouldBe(State.Processing);
        messageAfter.JobCount.ShouldBe(jobCountBefore);
    }
}
