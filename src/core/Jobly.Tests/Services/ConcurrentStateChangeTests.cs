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
    public async Task GivenConcurrentDeleteOnSameJob_ThenOnlyOneDeleteStateRecorded()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var tasks = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var service = TestUtils.CreateJobCommandService(CreateContext());
                    await service.DeleteJob(jobId);
                }
                catch
                {
                }
            }));
        }

        await Task.WhenAll(tasks);

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);

        // Only one Deleted JobLog entry should exist — not 10
        var deleteLogs = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Deleted")
            .CountAsync();
        deleteLogs.ShouldBe(1);
    }

    [Fact]
    public async Task GivenConcurrentRequeueOnSameCompletedJob_ThenSucceededDecrementedExactlyOnce()
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

        var tasks = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var service = TestUtils.CreateJobCommandService(CreateContext());
                    await service.RequeueJob(jobId);
                }
                catch
                {
                }
            }));
        }

        await Task.WhenAll(tasks);

        await TestUtils.AggregateCounters(CreateContext());

        // stats:succeeded should be decremented exactly once
        // First requeue: Completed -> Enqueued (stats:succeeded -1)
        // Subsequent requeues: Enqueued -> Enqueued (no stat change for Enqueued)
        var succeededAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        succeededAfter.ShouldBe(succeededBefore - 1);

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task GivenConcurrentRequeueOnSameJob_ThenJobStateEntriesAreConsistent()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // Enqueued -> Processing -> Completed (3 states)

        var logsBefore = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .CountAsync();

        var tasks = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var service = TestUtils.CreateJobCommandService(CreateContext());
                    await service.RequeueJob(jobId);
                }
                catch
                {
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Each successful requeue adds exactly one Requeued JobLog entry
        // Row lock ensures they run sequentially, so each sees the current state
        // At least the original logs + all requeue log entries
        // But the key point: total logs = logsBefore + number of successful requeues
        var logsAfter = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .CountAsync();

        logsAfter.ShouldBeGreaterThan(logsBefore);

        // Every JobLog should have a valid EventType
        var allEventTypes = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .Select(x => x.EventType)
            .ToListAsync();

        foreach (var eventType in allEventTypes)
        {
            eventType.ShouldNotBeNullOrEmpty();
        }
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

        // Concurrent delete + requeue on the same job
        var deleteTask = Task.Run(async () =>
        {
            try
            {
                await TestUtils.CreateJobCommandService(CreateContext()).DeleteJob(jobId);
            }
            catch
            {
            }
        });
        var requeueTask = Task.Run(async () =>
        {
            try
            {
                await TestUtils.CreateJobCommandService(CreateContext()).RequeueJob(jobId);
            }
            catch
            {
            }
        });
        await Task.WhenAll(deleteTask, requeueTask);

        await TestUtils.AggregateCounters(CreateContext());

        var job = await GetJob(jobId);
        var succeededAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // Completed state decremented exactly once regardless of operation order
        succeededAfter.ShouldBe(succeededBefore - 1);

        // Final state is valid
        (job.CurrentState == State.Deleted || job.CurrentState == State.Enqueued).ShouldBeTrue();
    }

    [Fact]
    public async Task GivenManyJobsDeletedConcurrently_ThenDeletedStatMatchesActualCount()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobIds = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            jobIds.Add(await publisher.Enqueue(new UnitRequest()));
        }

        await context.SaveChangesAsync();

        var deletedBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // Delete all 10 jobs concurrently
        var tasks = jobIds.ConvertAll(id => Task.Run(async () =>
        {
            var service = TestUtils.CreateJobCommandService(CreateContext());
            await service.DeleteJob(id);
        }));
        await Task.WhenAll(tasks);

        await TestUtils.AggregateCounters(CreateContext());

        var deletedAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // Exactly 10 deletes
        deletedAfter.ShouldBe(deletedBefore + 10);

        // All jobs should be Deleted
        foreach (var id in jobIds)
        {
            var job = await GetJob(id);
            job.CurrentState.ShouldBe(State.Deleted);
        }
    }

    [Fact]
    public async Task GivenRequeueAndProcessConcurrently_ThenNoCorruption()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        // Requeue and process concurrently — one modifies via service, other via worker
        var requeueTask = Task.Run(async () =>
        {
            try
            {
                await TestUtils.CreateJobCommandService(CreateContext()).RequeueJob(jobId);
            }
            catch
            {
            }
        });
        var processTask = Task.Run(async () =>
        {
            try
            {
                await ProcessJob();
            }
            catch
            {
            }
        });
        await Task.WhenAll(requeueTask, processTask);

        // Job should be in a valid state
        var job = await GetJob(jobId);
        job.ShouldNotBeNull();
        Enum.IsDefined<State>(job.CurrentState).ShouldBeTrue();
    }
}
