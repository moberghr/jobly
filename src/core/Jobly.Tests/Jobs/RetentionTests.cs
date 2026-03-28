using Jobly.Core;
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
    // ==================== ExpireAt Tests ====================

    [Fact]
    public async Task GivenCompletedJob_WhenProcessed_ThenExpireAtIsSet()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
        job.ExpireAt.ShouldNotBeNull();
        job.ExpireAt.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task GivenFailedJob_WhenProcessed_ThenExpireAtIsNull()
    {
        var context = CreateContext();

        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Failed);
        job.ExpireAt.ShouldBeNull();
    }

    [Fact]
    public async Task GivenDeletedJob_ThenExpireAtIsSet()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var service = TestUtils.CreateJoblyService(CreateContext());
        await service.DeleteJob(jobId);

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
        job.ExpireAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenCompletedMessage_WhenAllJobsDone_ThenMessageExpireAtIsSet()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        await ProcessJob(); // routes + executes

        var message = await GetMessage(messageId);
        message.CurrentState.ShouldBe(State.Completed);
        message.ExpireAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenFailingJobWithRetries_WhenReEnqueued_ThenFailedStatNotIncremented()
    {
        var context = CreateContext();

        var statBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // Create job with 2 retries — first failure re-enqueues, doesn't count as failed
        var jobId = await CreateFailedRetryJob(context, 2, null, null);

        await ProcessJob(); // fails, re-enqueued (retries remaining)

        var statAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        // Should NOT have incremented — retries not exhausted yet
        statAfter.ShouldBe(statBefore);

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
        job.RetriedTimes.ShouldBe(1);
    }

    // ==================== Statistics Tests ====================

    [Fact]
    public async Task GivenCompletedJob_WhenProcessed_ThenSucceededStatIncremented()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var statBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var statAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        statAfter.ShouldBe(statBefore + 1);
    }

    [Fact]
    public async Task GivenFailedJob_WhenRetriesExhausted_ThenFailedStatIncremented()
    {
        var context = CreateContext();

        var statBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var statAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        statAfter.ShouldBe(statBefore + 1);
    }

    [Fact]
    public async Task GivenDeletedJob_ThenDeletedStatIncremented()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var statBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var service = TestUtils.CreateJoblyService(CreateContext());
        await service.DeleteJob(jobId);

        var statAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        statAfter.ShouldBe(statBefore + 1);
    }

    [Fact]
    public async Task GivenDashboardStats_ThenHistoricalTotalsIncluded()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var service = TestUtils.CreateJoblyService(CreateContext());
        var stats = await service.GetJoblyStatus();

        stats.TotalSucceeded.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GivenFailedJobRequeued_ThenFailedStatDecremented()
    {
        var context = CreateContext();
        var jobId = await CreateFailedJob(context);

        await ProcessJob(); // fails, stats:failed +1

        var failedBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var service = TestUtils.CreateJoblyService(CreateContext());
        await service.RequeueJob(jobId); // requeue failed job → stats:failed -1

        var failedAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        failedAfter.ShouldBe(failedBefore - 1);
    }

    [Fact]
    public async Task GivenCompletedJobRequeued_ThenSucceededStatDecremented()
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

        var service = TestUtils.CreateJoblyService(CreateContext());
        await service.RequeueJob(jobId); // requeue → stats:succeeded -1

        var succeededAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        succeededAfter.ShouldBe(succeededBefore - 1);
    }

    [Fact]
    public async Task GivenCompletedJobDeleted_ThenSucceededDecrementedAndDeletedIncremented()
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

        var service = TestUtils.CreateJoblyService(CreateContext());
        await service.DeleteJob(jobId); // delete → stats:succeeded -1, stats:deleted +1

        var succeededAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        var deletedAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        succeededAfter.ShouldBe(succeededBefore - 1);
        deletedAfter.ShouldBe(deletedBefore + 1);
    }

    // ==================== Cleanup Tests ====================

    [Fact]
    public async Task GivenExpiredJob_WhenCleanupRuns_ThenJobIsDeletedFromDb()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // completes the job, sets ExpireAt

        // Manually set ExpireAt to the past to simulate expiration
        var updateContext = CreateContext();
        await updateContext.Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.ExpireAt, DateTime.UtcNow.AddHours(-1)));

        // Run actual cleanup code from HealthManager
        var cleaned = await JoblyHealthManager<TestContext>.RunCleanup(CreateContext());
        cleaned.ShouldBeGreaterThanOrEqualTo(1);

        // Job should be gone
        var deletedJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == jobId)
            .FirstOrDefaultAsync();
        deletedJob.ShouldBeNull();
    }

    [Fact]
    public async Task GivenExpiredJob_WhenCleanedUp_ThenStatisticsSurvive()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // completes, increments stats:succeeded

        var statsBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        statsBefore.ShouldBeGreaterThanOrEqualTo(1);

        // Expire and cleanup the job using actual HealthManager code
        var updateContext = CreateContext();
        await updateContext.Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.ExpireAt, DateTime.UtcNow.AddHours(-1)));

        await JoblyHealthManager<TestContext>.RunCleanup(CreateContext());

        // Stats should still be there after job deletion
        var statsAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        statsAfter.ShouldBe(statsBefore); // same value — survived deletion
    }

    [Fact]
    public async Task GivenFailedJob_WhenCleanupRuns_ThenJobSurvives()
    {
        var context = CreateContext();
        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        // Run actual cleanup from HealthManager — should not touch failed jobs
        var cleaned = await JoblyHealthManager<TestContext>.RunCleanup(CreateContext());
        cleaned.ShouldBe(0); // nothing to clean — failed jobs don't expire

        // Failed job still exists in DB
        var job = await GetJob(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Failed);
        job.ExpireAt.ShouldBeNull();
    }
}
