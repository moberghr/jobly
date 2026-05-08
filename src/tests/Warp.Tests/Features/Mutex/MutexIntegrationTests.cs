using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Enums;
using Warp.Core.Helper;
using Warp.Core.Mutex;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Features.Mutex;

[GenerateDatabaseTests]
public abstract class MutexIntegrationTestsBase : IntegrationTestBase
{
    protected MutexIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenTwoJobsWithSameMutex_WhenProcessed_ThenSecondIsCancelled()
    {
        var barrier = new BarrierSignal();

        await using var server = await WarpTestServer.StartAsync(Fixture, cfg => cfg.Services.AddSingleton(barrier));
        var publisher = server.CreatePublisher();

        // Enqueue a barrier-blocked job that holds the mutex while inside its handler
        var job1Id = await publisher.Enqueue(new BarrierRequest(), new JobParameters().WithMutex("test-mutex"));
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait for the handler to *enter* — MutexPipelineBehavior calls next() only after
        // TryAcquireAsync succeeds, so this signal proves the lock is held. Removes the race
        // window that a `WaitForJobState(Processing)` check has, where State=Processing is
        // committed *before* the pipeline runs.
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // Enqueue a second job with the same mutex — must short-circuit to Deleted
        var publisher2 = server.CreatePublisher();
        var job2Id = await publisher2.Enqueue(new UnitRequest(), new JobParameters().WithMutex("test-mutex"));
        await publisher2.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(job2Id, State.Deleted);

        // Verify job2 was cancelled due to mutex
        var logs = await server.GetJobLogs(job2Id);
        logs.ShouldContain(l => l.EventType == "Deleted" && l.Message.Contains("mutex"));

        // Job1 still in handler (held by barrier)
        var job1 = await server.GetJob(job1Id);
        job1.CurrentState.ShouldBe(State.Processing);

        // Release the barrier and let job1 complete naturally before disposal
        barrier.CanFinish.Release();
        await server.WaitForCompletion();
    }

    [TimedFact(20_000)]
    public async Task GivenTwoJobsWithSameMutex_WhenWaitMode_ThenSecondRequeuesUntilFirstFinishes()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();

        // Enqueue a slow job that holds the mutex
        var job1Id = await publisher.Enqueue(new CancellableRequest(), new JobParameters().WithMutex("test-wait", MutexMode.Wait));
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(job1Id, State.Processing);

        // Second job, same key, Wait mode — should be requeued, not deleted
        var publisher2 = server.CreatePublisher();
        var job2Id = await publisher2.Enqueue(new UnitRequest(), new JobParameters().WithMutex("test-wait", MutexMode.Wait));
        await publisher2.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Job2 should bounce off the mutex at least once and stay alive
        await server.WaitForJobLog(job2Id, "Requeued", timeout: TimeSpan.FromSeconds(5));

        var job2BeforeRelease = await server.GetJob(job2Id);
        job2BeforeRelease.CurrentState.ShouldNotBe(State.Deleted);
        job2BeforeRelease.CurrentState.ShouldNotBe(State.Completed);

        // Release job1 → job2 should now run to completion
        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(job1Id);
        await server.WaitForJobState(job1Id, State.Deleted, timeout: TimeSpan.FromSeconds(5));

        await server.WaitForJobState(job2Id, State.Completed, timeout: TimeSpan.FromSeconds(10));

        var requeuedLog = (await server.GetJobLogs(job2Id))
            .FirstOrDefault(x => string.Equals(x.EventType, "Requeued", StringComparison.Ordinal));
        requeuedLog.ShouldNotBeNull();
        requeuedLog.Message.ShouldContain("mutex");
        requeuedLog.Message.ShouldContain("test-wait");
    }

    [TimedFact(60_000)]
    public async Task GivenManyJobsWithSameMutex_WhenWaitMode_ThenAllRunSequentiallyToCompletion()
    {
        var tracker = new ConcurrencyTracker();

        await using var server = await WarpTestServer.StartAsync(
            Fixture,
            configure: null,
            configureServices: services => services.AddSingleton(tracker));

        var publisher = server.CreatePublisher();

        const string key = "serial-key";
        const int jobCount = 10;
        var jobIds = new List<Guid>(jobCount);
        for (var i = 0; i < jobCount; i++)
        {
            var id = await publisher.Enqueue(
                new ConcurrencyTrackerRequest { Key = key },
                new JobParameters().WithMutex(key, MutexMode.Wait));
            jobIds.Add(id);
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForCompletion(timeout: TimeSpan.FromSeconds(45));

        // None deleted, none failed — all should be Completed
        foreach (var id in jobIds)
        {
            var job = await server.GetJob(id);
            job.CurrentState.ShouldBe(State.Completed);
        }

        tracker.Completed.ShouldBe(jobCount);
        tracker.MaxObserved.ShouldBe(1, "Wait mode must guarantee at most one job per key runs at a time");
    }

    [TimedFact(60_000)]
    public async Task GivenJobsWithDifferentKeys_WhenWaitMode_ThenKeysRunInParallelButEachKeySerialized()
    {
        var tracker = new ConcurrencyTracker();

        await using var server = await WarpTestServer.StartAsync(
            Fixture,
            configure: null,
            configureServices: services => services.AddSingleton(tracker));

        var publisher = server.CreatePublisher();

        const int keyCount = 5;
        const int jobsPerKey = 3;
        var keys = Enumerable.Range(0, keyCount).Select(i => $"parallel-key-{i}").ToArray();

        var jobIds = new List<Guid>(keyCount * jobsPerKey);
        foreach (var key in keys)
        {
            for (var j = 0; j < jobsPerKey; j++)
            {
                var id = await publisher.Enqueue(
                    new ConcurrencyTrackerRequest { Key = key },
                    new JobParameters().WithMutex(key, MutexMode.Wait));
                jobIds.Add(id);
            }
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForCompletion(timeout: TimeSpan.FromSeconds(45));

        foreach (var id in jobIds)
        {
            var job = await server.GetJob(id);
            job.CurrentState.ShouldBe(State.Completed);
        }

        tracker.Completed.ShouldBe(keyCount * jobsPerKey);

        // Per-key serialization holds — at most one job per key was ever in flight.
        foreach (var key in keys)
        {
            tracker.CompletedFor(key).ShouldBe(jobsPerKey);
            tracker.MaxObservedFor(key).ShouldBe(1, $"key '{key}' must serialize");
        }

        // Across keys, parallelism actually happened — at some point at least 2 keys ran
        // concurrently. We can't reliably assert MaxObserved == keyCount because worker count
        // / scheduling jitter may stagger them, but observing > 1 proves keys aren't globally
        // serialized. Default WorkerCount is min(ProcessorCount * 5, 20) so headroom is ample.
        tracker.MaxObserved.ShouldBeGreaterThan(1, "different keys must be allowed to run in parallel");
    }
}
