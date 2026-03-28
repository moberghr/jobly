using Jobly.Core;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task Publish_RetryJobWithStateFailed_RetriedTimesShouldBeEqualToMaxRetries()
    {
        const int retries = 5;
        var context = CreateContext();
        var jobId = await CreateFailedRetryJob(context, retries, null, null);

        for (var i = 0; i <= 10; i++)
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
        const int retries = 0;
        var context = CreateContext();
        var jobId = await CreateFailedRetryJob(context, retries, null, null);

        for (var i = 0; i <= 10; i++)
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
        const int retries = 0;
        var context = CreateContext();
        const int maxRetries = 2;
        var jobId = await CreateFailedRetryJob(context, retries, maxRetries, null);

        for (var i = 0; i <= 10; i++)
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
        const int retries = 5;
        var context = CreateContext();
        const int maxRetries = 1;
        var jobId = await CreateFailedRetryJob(context, retries, maxRetries, null);

        for (var i = 0; i <= 10; i++)
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
        const int retries = 0;
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context, retries);
        var jobRequest = new UnitRequest();
        var jobId = await publisher.Enqueue(jobRequest);

        await context.SaveChangesAsync();

        for (var i = 0; i <= 10; i++)
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
        const int retries = 5;
        const int successIteration = 3;
        var context = CreateContext();
        var jobId = await CreateFailedRetryJob(context, retries, null, null);

        for (var i = 0; i <= 10; i++)
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
