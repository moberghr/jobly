using Handfire.Core.Enums;
using Handfire.Core;
using Handfire.Tests.TestData.Handlers;
using Shouldly;

namespace Handfire.Tests.Jobs;

public abstract partial class JobPublisher: TestBase
{
    [Fact]
    public async Task Publish_RetryJobWithStateFailed_RetriedTimesShouldBeEqualToMaxRetries()
    {
        int retries = 5;
        var context = CreateContext();
        string jobId = await CreateFailedRetryJob(context, retries, null);

        for (int i = 0; i <= 10; i++)
        {
            await ProcessJob();
        }

        var currentJob = await GetJob(jobId);

        currentJob.ShouldNotBeNull();
        currentJob.CurrentState.ShouldBe(State.Failed);
        currentJob.MaxRetries.ShouldBe(retries);
        currentJob.RetriedTimes.ShouldBe(retries);
    }

    [Fact]
    public async Task Publish_WithoutRetryJob_WithStateFailed()
    {
        int retries = 0;
        var context = CreateContext();
        string jobId = await CreateFailedRetryJob(context, retries, null);

        for (int i = 0; i <= 10; i++)
        {
            await ProcessJob();
        }

        var currentJob = await GetJob(jobId);

        currentJob.ShouldNotBeNull();
        currentJob.CurrentState.ShouldBe(State.Failed);
        currentJob.MaxRetries.ShouldBe(retries);
        currentJob.RetriedTimes.ShouldBe(retries);
    }

    [Fact]
    public async Task Publish_RetryJob_UsePublisherMaxRetriesParameter()
    {
        int retries = 0;
        var context = CreateContext();
        int maxRetries = 2;
        string jobId = await CreateFailedRetryJob(context, retries, maxRetries);

        for (int i = 0; i <= 10; i++)
        {
            await ProcessJob();
        }

        var currentJob = await GetJob(jobId);

        currentJob.ShouldNotBeNull();
        currentJob.CurrentState.ShouldBe(State.Failed);
        currentJob.MaxRetries.ShouldBe(maxRetries);
        currentJob.RetriedTimes.ShouldBe(maxRetries);
    }

    [Fact]
    public async Task Publish_RetryJob_UsePublisherMaxRetriesAndGlobalRetryParameter()
    {
        int retries = 5;
        var context = CreateContext();
        int maxRetries = 1;
        string jobId = await CreateFailedRetryJob(context, retries, maxRetries);

        for (int i = 0; i <= 10; i++)
        {
            await ProcessJob();
        }

        var currentJob = await GetJob(jobId);

        currentJob.ShouldNotBeNull();
        currentJob.CurrentState.ShouldBe(State.Failed);
        currentJob.MaxRetries.ShouldBe(maxRetries);
        currentJob.RetriedTimes.ShouldBe(maxRetries);
    }

    [Fact]
    public async Task Publish_WithoutRetryJob_WithStateComplited()
    {
        int retries = 0;
        var context = CreateContext();
        var publisher = new Publisher<TestContext>(context, retries);
        var jobRequest = new UnitRequest();
        string jobId = await publisher.Publish(jobRequest);

        await context.SaveChangesAsync();

        for (int i = 0; i <= 10; i++)
        {
            await ProcessJob();
        }

        var currentJob = await GetJob(jobId);

        currentJob.ShouldNotBeNull();
        currentJob.CurrentState.ShouldBe(State.Completed);
        currentJob.MaxRetries.ShouldBe(retries);
        currentJob.RetriedTimes.ShouldBe(retries);
    }

    [Fact]
    public async Task Publish_RetryJobWithStateComplated_RetriedTimesShouldNotBeEqualToMaxRetries()
    {
        int retries = 5;
        int successIteration = 3;
        var context = CreateContext();
        string jobId = await CreateFailedRetryJob(context, retries, null);

        for (int i = 0; i <= 10; i++)
        {
            if (i == successIteration)
            {
                await ChangeJobFromException(jobId);
            }

            await ProcessJob();
        }

        var currentJob = await GetJob(jobId);

        currentJob.ShouldNotBeNull();
        currentJob.CurrentState.ShouldBe(State.Completed);
        currentJob.MaxRetries.ShouldBe(retries);
        currentJob.RetriedTimes.ShouldBe(successIteration);
    }
}
