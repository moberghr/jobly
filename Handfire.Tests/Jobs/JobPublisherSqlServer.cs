using System.Text.Json;
using Handfire.Core;
using Handfire.Core.Enums;
using Handfire.Tests.TestData.Handlers;

namespace Handfire.Tests.Jobs;

public class JobPublisherSqlServer : SqlServerTestBase
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

        Assert.NotNull(jobFromDb);
        Assert.Equal(State.Enqueued, jobFromDb.CurrentState);
        Assert.Equal(jobRequest.GetType().AssemblyQualifiedName!, jobFromDb.Type);
        Assert.Equal(JsonSerializer.Serialize(jobRequest), jobFromDb.Message);

        Assert.Single(jobFromDb.JobStates);
        Assert.Equal(State.Enqueued, jobFromDb.JobStates.Single().State);
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

        Assert.Equal(State.Completed, jobFromDb.CurrentState);
        Assert.Equal(2, jobFromDb.JobStates.Count);

        Assert.Equal(State.Enqueued, jobFromDb.JobStates.First().State);
        Assert.Equal(State.Completed, jobFromDb.JobStates.Last().State);

        Assert.NotNull(logFromDb.ProcessedTime);
    }

    [Fact]
    public async Task GetAndProcessJob_JobThrowsException_ShouldBeFailed()
    {
        var context = CreateContext();

        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var jobFromDb = await GetJobWithStates(context, jobId);

        Assert.Equal(State.Failed, jobFromDb.CurrentState);

        Assert.Equal(2, jobFromDb.JobStates.Count);
        Assert.Equal(State.Enqueued, jobFromDb.JobStates.First().State);
        Assert.Equal(State.Failed, jobFromDb.JobStates.Last().State);
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
        Assert.NotEqual(1, counter);
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
        Assert.Equal(1, counter);
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

        Assert.NotNull(currentJob);
        Assert.Equal(State.Failed, currentJob.CurrentState);
        Assert.Equal(retries, currentJob.MaxRetries);
        Assert.Equal(retries, currentJob.RetriedTimes);
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

        Assert.NotNull(currentJob);
        Assert.Equal(State.Failed, currentJob.CurrentState);
        Assert.Equal(retries, currentJob.MaxRetries);
        Assert.Equal(retries, currentJob.RetriedTimes);
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

        Assert.NotNull(currentJob);
        Assert.Equal(State.Failed, currentJob.CurrentState);
        Assert.Equal(maxRetries, currentJob.MaxRetries);
        Assert.Equal(maxRetries, currentJob.RetriedTimes);
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

        Assert.NotNull(currentJob);
        Assert.Equal(State.Failed, currentJob.CurrentState);
        Assert.Equal(maxRetries, currentJob.MaxRetries);
        Assert.Equal(maxRetries, currentJob.RetriedTimes);
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

        Assert.NotNull(currentJob);
        Assert.Equal(State.Completed, currentJob.CurrentState);
        Assert.Equal(retries, currentJob.MaxRetries);
        Assert.Equal(retries, currentJob.RetriedTimes);
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

        Assert.NotNull(currentJob);
        Assert.Equal(State.Completed, currentJob.CurrentState);
        Assert.Equal(retries, currentJob.MaxRetries);
        Assert.Equal(successIteration, currentJob.RetriedTimes);
    }
}
