using Handfire.Core.Enums;
using Handfire.Core;
using Handfire.Tests.TestData.Handlers;
using System.Text.Json;
using Shouldly;

namespace Handfire.Tests.Jobs;

public abstract class JobPublisher : TestBase
{
    [Fact]
    public async Task Publish_AddJob_ShouldHaveCreatedStatusInDb()
    {
        var context = CreateContext();

        var publisher = new Publisher<TestContext>(context, 0);
        var jobRequest = new UnitRequest();
        var jobId = await publisher.Publish(jobRequest);

        await context.SaveChangesAsync();

        var jobFromDb = await GetJobWithStates(context, jobId);

        jobFromDb.ShouldNotBeNull();
        jobFromDb.CurrentState.ShouldBe(State.Enqueued);
        jobFromDb.Type.ShouldBe(jobRequest.GetType().AssemblyQualifiedName!);
        jobFromDb.Message.ShouldBe(JsonSerializer.Serialize(jobRequest));
        jobFromDb.JobStates.ShouldHaveSingleItem();
        jobFromDb.JobStates.Single().State.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task GetAndProcessJob_ProcessCreatedJob_ShouldBeCompleted()
    {
        var context = CreateContext();

        var testLogId = await CreateLogInDb(context);

        var jobId = await CreateProcessLogJob(context, testLogId);

        await ProcessJob();

        var jobFromDb = await GetJobWithStates(context, jobId);
        var logFromDb = await GetTestLog(context, testLogId);
        jobFromDb.CurrentState.ShouldBe(State.Completed);
        jobFromDb.JobStates.Count.ShouldBe(2);
        jobFromDb.JobStates.First().State.ShouldBe(State.Enqueued);
        jobFromDb.JobStates.Last().State.ShouldBe(State.Completed);
        logFromDb.ProcessedTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetAndProcessJob_JobThrowsException_ShouldBeFailed()
    {
        var context = CreateContext();

        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var jobFromDb = await GetJobWithStates(context, jobId);

        jobFromDb.CurrentState.ShouldBe(State.Failed);
        jobFromDb.JobStates.Count.ShouldBe(2);
        jobFromDb.JobStates.First().State.ShouldBe(State.Enqueued);
        jobFromDb.JobStates.Last().State.ShouldBe(State.Failed);
    }

    [Fact]
    public async Task GetAndProcessJob_WithoutLockingInterceptor_CounterShouldBeMoreThenOne()
    {
        await CreateCounterJob();

        List<Task> tasks = new();
        for (var i = 0; i < 20; i++)
        {
            tasks.Add(ProcessJobWithoutLocking());
        }

        Task.WaitAll(tasks.ToArray());


        var counter = await GetCounterForNoLocking();
        counter.ShouldNotBe(1);
    }

    [Fact]
    public async Task GetAndProcessJob_JobWithCounter_CounterShouldBeOne()
    {
        await CreateCounterJob();

        List<Task> tasks = new();
        for (var i = 0; i < 20; i++)
        {
            tasks.Add(ProcessJob());
        }

        Task.WaitAll(tasks.ToArray());

        var counter = await GetCounter();
        counter.ShouldBe(1);
    }

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

