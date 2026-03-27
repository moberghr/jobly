using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenMultipleJobsWithDifferentPriorities_WhenProcessed_ThenHighestPriorityJobIsProcessedFirst()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var lowJobId = await publisher.Enqueue(new UnitRequest(), Priority.Low);
        var normalJobId = await publisher.Enqueue(new UnitRequest(), Priority.Normal);
        var urgentJobId = await publisher.Enqueue(new UnitRequest(), Priority.Urgent);

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
    public async Task GivenSamePriorityJobs_WhenProcessed_ThenEarlierScheduledJobIsProcessedFirst()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var earlierJobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddSeconds(-10), Priority.Normal);
        var laterJobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddSeconds(-1), Priority.Normal);

        await context.SaveChangesAsync();

        await ProcessJob();

        var earlierJob = await GetJob(earlierJobId);
        var laterJob = await GetJob(laterJobId);

        earlierJob.CurrentState.ShouldBe(State.Completed);
        laterJob.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task GivenUrgentFutureJobAndLowPastJob_WhenProcessed_ThenOnlyEligibleJobIsProcessed()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var urgentFutureJobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddHours(1), Priority.Urgent);
        var lowPastJobId = await publisher.Schedule(new UnitRequest(), DateTime.UtcNow.AddSeconds(-1), Priority.Low);

        await context.SaveChangesAsync();

        await ProcessJob();

        var urgentFutureJob = await GetJob(urgentFutureJobId);
        var lowPastJob = await GetJob(lowPastJobId);

        lowPastJob.CurrentState.ShouldBe(State.Completed);
        urgentFutureJob.CurrentState.ShouldBe(State.Enqueued);
    }
}
