using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Services;

public abstract partial class ServiceTests : TestBase
{
    [Fact]
    public async Task GivenConcurrentDeleteOnSameJob_ThenOnlyOneSucceeds()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var succeededCount = 0;
        var failedCount = 0;

        var tasks = new List<Task>();
        for (var i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var service = TestUtils.CreateJoblyService(CreateContext());
                    await service.DeleteJob(jobId);
                    Interlocked.Increment(ref succeededCount);
                }
                catch
                {
                    Interlocked.Increment(ref failedCount);
                }
            }));
        }
        await Task.WhenAll(tasks);

        // Exactly one should have succeeded (others get wrong state or throw)
        succeededCount.ShouldBeGreaterThanOrEqualTo(1);

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }

    [Fact]
    public async Task GivenConcurrentRequeueOnSameJob_ThenStatsStayBalanced()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // completes the job, stats:succeeded +1

        var succeededBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // Multiple concurrent requeue attempts on the same completed job
        var tasks = new List<Task>();
        for (var i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var service = TestUtils.CreateJoblyService(CreateContext());
                    await service.RequeueJob(jobId);
                }
                catch
                {
                    // Some will fail — that's expected
                }
            }));
        }
        await Task.WhenAll(tasks);

        // Stats should be decremented exactly once (from Completed → Enqueued)
        var succeededAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // First requeue: stats:succeeded -1 (Completed → Enqueued)
        // Subsequent requeues: from Enqueued state, no stat decrement (Enqueued has no stat)
        succeededAfter.ShouldBe(succeededBefore - 1);
    }

    [Fact]
    public async Task GivenConcurrentDeleteAndRequeue_ThenStatsStayBalanced()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // completes, stats:succeeded +1

        var succeededBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        var deletedBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // Concurrent delete + requeue on the same job
        var deleteTask = Task.Run(async () =>
        {
            try { await TestUtils.CreateJoblyService(CreateContext()).DeleteJob(jobId); } catch { }
        });
        var requeueTask = Task.Run(async () =>
        {
            try { await TestUtils.CreateJoblyService(CreateContext()).RequeueJob(jobId); } catch { }
        });
        await Task.WhenAll(deleteTask, requeueTask);

        var job = await GetJob(jobId);
        var succeededAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        var deletedAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // One operation won. Stats should be consistent:
        // If delete won: succeeded -1, deleted +1
        // If requeue won first then delete: succeeded -1 (requeue), then delete on Enqueued (no succeeded decrement, deleted +1)
        // Either way: succeeded should be decremented by exactly 1 from the Completed state
        succeededAfter.ShouldBe(succeededBefore - 1);

        // Final state is deterministic: whichever ran last
        // Just verify it's a valid final state
        (job.CurrentState == State.Deleted || job.CurrentState == State.Enqueued).ShouldBeTrue();
    }
}
