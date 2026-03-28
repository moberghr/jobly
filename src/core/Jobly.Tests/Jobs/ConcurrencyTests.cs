using Jobly.Tests.TestData.Handlers;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenMultipleJobsAndMultipleWorkers_WhenProcessedConcurrently_ThenEachJobProcessedExactlyOnce()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        for (var i = 0; i < 10; i++)
        {
            await publisher.Enqueue(new CounterRequest());
        }

        await context.SaveChangesAsync();

        await ProcessAllJobs(workerCount: 5);

        var counter = await GetCounter();
        counter.ShouldBe(10);
    }

    [Fact]
    public async Task GivenOneJobAndMultipleWorkers_WhenProcessedConcurrently_ThenOnlyOneWorkerProcessesIt()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Enqueue(new CounterRequest());
        await context.SaveChangesAsync();

        await ProcessAllJobs(workerCount: 5);

        var counter = await GetCounter();
        counter.ShouldBe(1);
    }

    [Fact]
    public async Task GivenOneMessageAndMultipleWorkers_WhenRoutedConcurrently_ThenMessageRoutedExactlyOnce()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        await ProcessAllJobs(workerCount: 5);

        var jobs = await GetJobsForMessage(messageId);
        jobs.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenMessageWithMultipleHandlers_WhenProcessedConcurrently_ThenBothHandlersExecuteExactlyOnce()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        await ProcessAllJobs(workerCount: 5);

        var counter = GetMultiHandlerCounter();
        counter.CountA.ShouldBe(1);
        counter.CountB.ShouldBe(1);
    }

    [Fact]
    public async Task GivenMultipleMessagesAndJobs_WhenProcessedConcurrently_ThenAllProcessedCorrectly()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Publish(new MultiRequest());              // 1 message → 2 handler jobs
        await publisher.Publish(new SingleHandlerMessage());      // 1 message → 1 handler job
        await publisher.Enqueue(new CounterRequest());            // 1 direct job
        await publisher.Enqueue(new CounterRequest());            // 1 direct job
        await context.SaveChangesAsync();

        await ProcessAllJobs(workerCount: 5);

        var jobCounter = await GetCounter();
        jobCounter.ShouldBe(2);

        // CountA=2: MultiHandlerA + SingleMessageHandler both increment CountA
        // CountB=1: only MultiHandlerB
        var multiCounter = GetMultiHandlerCounter();
        multiCounter.CountA.ShouldBe(2);
        multiCounter.CountB.ShouldBe(1);
    }
}
