using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenMultipleJobsWithDifferentQueues_WhenProcessed_ThenFirstAlphabeticalQueueJobIsProcessedFirst()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var lowJobId = await publisher.Enqueue(new UnitRequest(), "c-low");
        var normalJobId = await publisher.Enqueue(new UnitRequest(), "b-default");
        var urgentJobId = await publisher.Enqueue(new UnitRequest(), "a-critical");

        await context.SaveChangesAsync();

        await ProcessJob();

        var urgentJob = await GetJob(urgentJobId);
        var normalJob = await GetJob(normalJobId);
        var lowJob = await GetJob(lowJobId);

        urgentJob.CurrentState.ShouldBe(State.Completed);
        normalJob.CurrentState.ShouldBe(State.Enqueued);
        lowJob.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task GivenJobScheduledInFuture_WhenProcessed_ThenJobIsNotPickedUp()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddHours(1));

        await context.SaveChangesAsync();

        var result = await TryProcessJob();

        result.ShouldBeFalse();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task GivenJobScheduledInPast_WhenProcessed_ThenJobIsCompleted()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddSeconds(-1));

        await context.SaveChangesAsync();

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenSameQueueJobs_WhenProcessed_ThenEarlierScheduledJobIsProcessedFirst()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var earlierJobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddSeconds(-10), "b-default");
        var laterJobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddSeconds(-1), "b-default");

        await context.SaveChangesAsync();

        await ProcessJob();

        var earlierJob = await GetJob(earlierJobId);
        var laterJob = await GetJob(laterJobId);

        earlierJob.CurrentState.ShouldBe(State.Completed);
        laterJob.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task GivenCriticalFutureJobAndLowPastJob_WhenProcessed_ThenOnlyEligibleJobIsProcessed()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var criticalFutureJobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddHours(1), "a-critical");
        var lowPastJobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddSeconds(-1), "c-low");

        await context.SaveChangesAsync();

        await ProcessJob();

        var criticalFutureJob = await GetJob(criticalFutureJobId);
        var lowPastJob = await GetJob(lowPastJobId);

        lowPastJob.CurrentState.ShouldBe(State.Completed);
        criticalFutureJob.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task GivenJobInUnsubscribedQueue_WhenProcessed_ThenJobIsNotPickedUp()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        // "zzz-unsubscribed" is not in the worker's Queues config
        var jobId = await publisher.Enqueue(new UnitRequest(), "zzz-unsubscribed");
        await context.SaveChangesAsync();

        var result = await TryProcessJob();
        result.ShouldBeFalse();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task GivenJobDefaultQueue_WhenProcessed_ThenJobIsPickedUp()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        // "default" is in the worker's Queues config
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenMessageInQueue_WhenRouted_ThenJobsInheritQueue()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new SingleHandlerMessage(), "a-critical");
        await context.SaveChangesAsync();

        await RouteMessages();

        var jobs = await GetJobsForMessage(messageId);
        jobs.Count.ShouldBe(1);
        jobs[0].Queue.ShouldBe("a-critical");
    }
}
