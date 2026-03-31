using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenMessage_WhenPublished_ThenMessageCreatedWithEnqueuedState()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        var message = await GetMessage(messageId);
        message.CurrentState.ShouldBe(State.Enqueued);
        message.Type.ShouldContain(nameof(SingleHandlerMessage));
    }

    [Fact]
    public async Task GivenMessage_WhenRouted_ThenJobCreatedWithMessageId()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        await RouteMessages();

        var jobs = await GetJobsForMessage(messageId);
        jobs.Count.ShouldBe(1);
        jobs[0].ParentJobId.ShouldBe(messageId);
        jobs[0].HandlerType.ShouldContain(nameof(SingleMessageHandler));
    }

    [Fact]
    public async Task GivenMessageWithSingleHandler_WhenProcessed_ThenHandlerExecutes()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var counter = GetMultiHandlerCounter();
        var beforeCount = counter.CountA;

        await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        counter.CountA.ShouldBe(beforeCount + 1);
    }

    [Fact]
    public async Task GivenMessageWithSingleHandler_WhenProcessed_ThenMessageIsCompleted()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        var message = await GetMessage(messageId);
        message.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenMessageWithMultipleHandlers_WhenPartiallyProcessed_ThenMessageStaysProcessing()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // Route message
        await RouteMessages();

        // Execute only first handler job
        await ProcessJob();

        // Orchestrate — should see 1 child still not terminal, so message stays Processing
        await RunOrchestration();

        var message = await GetMessage(messageId);
        message.CurrentState.ShouldBe(State.Processing);
    }

    [Fact]
    public async Task GivenMessageWithQueue_WhenRouted_ThenJobsInheritQueue()
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

    [Fact]
    public async Task GivenJob_WhenEnqueued_ThenJobCreatedDirectlyWithNoMessageId()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var job = await GetJob(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
        job.ParentJobId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenJob_WhenEnqueued_ThenNoMessageRowCreated()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var messageCount = await CreateContext().Set<Job>().Where(x => x.Kind == JobKind.Message).CountAsync();
        messageCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenJob_WhenScheduledInFuture_ThenNotPickedUpUntilScheduleTime()
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
    public async Task GivenJob_WhenProcessed_ThenHandlerExecutesDirectly()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var testLogId = await CreateLogInDb(context);
        var jobId = await publisher.Enqueue(new PrecessLogRequest { TestTaskId = testLogId });
        await context.SaveChangesAsync();

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);

        var log = await GetTestLog(context, testLogId);
        log.ProcessedTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenJob_WhenProcessed_ThenHandlerTypeIsSetOnJob()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var job = await GetJob(jobId);
        job.HandlerType.ShouldNotBeNull();
        job.HandlerType.ShouldContain(nameof(UnitCommand));
    }

    [Fact]
    public async Task GivenJobAndMessage_WhenWorkerPolls_ThenBothAreProcessed()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new SingleHandlerMessage());
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);

        var message = await GetMessage(messageId);
        message.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenMultipleHandlers_WhenOneJobFails_ThenOtherJobStillCompletes()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // Route message (creates 2 jobs)
        await RouteMessages();

        // Execute first handler job only
        await ProcessJob();

        var jobs = await GetJobsForMessage(messageId);
        jobs.Count.ShouldBe(2);

        // One should be Completed, the other still Enqueued — independent lifecycle
        jobs.ShouldContain(j => j.CurrentState == State.Completed);
        jobs.ShouldContain(j => j.CurrentState == State.Enqueued);
    }
}
