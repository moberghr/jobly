using Handfire.Core.Enums;
using Handfire.Core;
using Handfire.Tests.TestData.Handlers;
using System.Text.Json;
using Shouldly;
using Moq;
using Handfire.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Tests.Jobs;

public abstract partial class HandfireTests : TestBase
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
    public async Task GivenGetAndProcessJob_WhenJobIsBeingUpdatedWhileHavingBatch_ThenCounterShouldBeUpdated()
    {
        var context = CreateContext();

        var publisher = new Publisher<TestContext>(context, 0);

        var requestAndJobDatas = new List<RequestAndJobStateData>();

        for (int i = 0; i < 10; i++)
        {
            var request = new UnitRequest();
            var jobState = await publisher.CreateJobAndJobState(request, name: string.Empty, scheduleTime: null, maxRetries: null, null);


            var requestAndJobData = new RequestAndJobStateData
            {
                JobState = jobState,
                Request = request,
            };

            requestAndJobDatas.Add(requestAndJobData);
        }

        var _mockPublisher = new Mock<IPublisher>();

        foreach (var requestAndJobData in requestAndJobDatas)
        {
            _mockPublisher.Setup(x => x.CreateJobAndJobState(requestAndJobData.Request, string.Empty, null, null, null))
                .ReturnsAsync(requestAndJobData.JobState);
        }

        var batchPublisher = new BatchPublisher<TestContext>(context, _mockPublisher.Object);

        var requests = requestAndJobDatas.Select(x => x.Request).ToList();

        await batchPublisher.AddBatchAndBatchContinuationJobs(requests, requests);

        await ProcessJob();

        var batch = await CreateContext().Set<Batch>().FirstAsync();

        batch.Counter.ShouldBe(9);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenAllJobsInBatchAreFinished_ThenCounterShouldBeZero()
    {
        var context = CreateContext();

        var publisher = new Publisher<TestContext>(context, 0);

        var requestAndJobDatas = new List<RequestAndJobStateData>();

        for (int i = 0; i < 2; i++)
        {
            var request = new UnitRequest();
            var jobState = await publisher.CreateJobAndJobState(request, name: string.Empty, scheduleTime: null, maxRetries: null, null);


            var requestAndJobData = new RequestAndJobStateData
            {
                JobState = jobState,
                Request = request,
            };

            requestAndJobDatas.Add(requestAndJobData);
        }

        var _mockPublisher = new Mock<IPublisher>();

        foreach (var requestAndJobData in requestAndJobDatas)
        {
            _mockPublisher.Setup(x => x.CreateJobAndJobState(requestAndJobData.Request, string.Empty, null, null, null))
                .ReturnsAsync(requestAndJobData.JobState);
        }

        var batchPublisher = new BatchPublisher<TestContext>(context, _mockPublisher.Object);

        var requests = requestAndJobDatas.Select(x => x.Request).ToList();

        await batchPublisher.AddBatchAndBatchContinuationJobs(requests, requests);

        await ProcessJob();
        await ProcessJob();

        var batch = await CreateContext().Set<Batch>().FirstAsync();

        batch.Counter.ShouldBe(0);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenAllJobsInBatchAreFinished_ThenBatchStatusShouldBeUpdatedToCompleted()
    {
        var context = CreateContext();

        var publisher = new Publisher<TestContext>(context, 0);

        var requestAndJobDatas = new List<RequestAndJobStateData>();

        for (int i = 0; i < 2; i++)
        {
            var request = new UnitRequest();
            var jobState = await publisher.CreateJobAndJobState(request, name: string.Empty, scheduleTime: null, maxRetries: null, null);


            var requestAndJobData = new RequestAndJobStateData
            {
                JobState = jobState,
                Request = request,
            };

            requestAndJobDatas.Add(requestAndJobData);
        }

        var _mockPublisher = new Mock<IPublisher>();

        foreach (var requestAndJobData in requestAndJobDatas)
        {
            _mockPublisher.Setup(x => x.CreateJobAndJobState(requestAndJobData.Request, string.Empty, null, null, null))
                .ReturnsAsync(requestAndJobData.JobState);
        }

        var batchPublisher = new BatchPublisher<TestContext>(context, _mockPublisher.Object);

        var requests = requestAndJobDatas.Select(x => x.Request).ToList();

        await batchPublisher.AddBatchAndBatchContinuationJobs(requests, requests);

        await ProcessJob();
        await ProcessJob();

        var batch = await CreateContext().Set<Batch>().FirstAsync();

        batch.BatchStatus.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenAllJobsInBatchAreFinished_ThenAllJobsCurrentStateInBatchContinuationShouldBeUpdatedToEnqueued()
    {
        var context = CreateContext();

        var publisher = new Publisher<TestContext>(context, 0);

        var requestAndJobDatas = new List<RequestAndJobStateData>();

        for (int i = 0; i < 2; i++)
        {
            var request = new UnitRequest();
            var jobState = await publisher.CreateJobAndJobState(request, name: string.Empty, scheduleTime: null, maxRetries: null, null);


            var requestAndJobData = new RequestAndJobStateData
            {
                JobState = jobState,
                Request = request,
            };

            requestAndJobDatas.Add(requestAndJobData);
        }

        var _mockPublisher = new Mock<IPublisher>();

        foreach (var requestAndJobData in requestAndJobDatas)
        {
            _mockPublisher.Setup(x => x.CreateJobAndJobState(requestAndJobData.Request, string.Empty, null, null, null))
                .ReturnsAsync(requestAndJobData.JobState);
        }

        var batchPublisher = new BatchPublisher<TestContext>(context, _mockPublisher.Object);

        var requests = requestAndJobDatas.Select(x => x.Request).ToList();

        await batchPublisher.AddBatchAndBatchContinuationJobs(requests, requests);

        await ProcessJob();
        await ProcessJob();

        var batchContinuationJobs = await CreateContext().Set<BatchContinuation>()
            .Select(x => x.Job)
            .ToListAsync();

        foreach (var batchContinuationJob in batchContinuationJobs)
        {
            batchContinuationJob.CurrentState.ShouldBe(State.Enqueued);
        }
    }
}

