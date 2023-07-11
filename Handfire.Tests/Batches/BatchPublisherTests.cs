using Handfire.Core;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Moq;
using Shouldly;

namespace Handfire.Tests.Jobs;

public abstract partial class HandfireTests : TestBase
{
    [Fact]
    public async Task GivenAddBatchAndBatchContinuationJobs_WhenNewBatchIsCreated_ThenNewBatchIsCreated()
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

        var newBatchData = await context.Set<Batch>()
            .Select(x =>
                new
                {
                    Batch = x,
                    Jobs = x.Jobs,
                    BatchContinuations = x.BatchContinuations,
                })
            .FirstOrDefaultAsync();

        newBatchData.ShouldNotBeNull();

        newBatchData.Batch.BatchStatus.ShouldBe(State.Enqueued);
        newBatchData.Batch.Counter.ShouldBe(10);

        newBatchData.Jobs.Count.ShouldBe(10);

        foreach (var requestAndJobData in requestAndJobDatas)
        {
            var batchJob = newBatchData.Jobs.Where(x => x.Id == requestAndJobData.JobState.JobId).FirstOrDefault();

            batchJob.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task GivenAddBatchAndBatchContinuationJobs_WhenNewBatchIsCreated_ThenNewBatchContinuationIsCreated()
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

        await context.SaveChangesAsync();

        var newBatchContinuations = await context.Set<BatchContinuation>()
            .ToListAsync();

        newBatchContinuations.Count.ShouldBe(10);

        foreach (var requestAndJobData in requestAndJobDatas)
        {
            var batchContinationJob = newBatchContinuations.Where(x => x.JobId == requestAndJobData.JobState.JobId).FirstOrDefault();

            batchContinationJob.ShouldNotBeNull();
        }
    }

    private class RequestAndJobStateData
    {
        public UnitRequest Request { get; set; } = null!;

        public JobState JobState { get; set; } = null!;
    }
}
