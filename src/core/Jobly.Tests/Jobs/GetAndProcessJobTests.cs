using System.Text.Json;
using Jobly.Core;
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
    public async Task Publish_AddJob_ShouldHaveCreatedStatusInDb()
    {
        var context = CreateContext();

        var publisher = TestUtils.CreatePublisher(context);
        var jobRequest = new UnitRequest();
        var jobId = await publisher.Enqueue(jobRequest);

        await context.SaveChangesAsync();

        var jobFromDb = await GetJob(jobId);

        jobFromDb.ShouldNotBeNull();
        jobFromDb.CurrentState.ShouldBe(State.Enqueued);
        jobFromDb.Type.ShouldBe(jobRequest.GetType().AssemblyQualifiedName!);
        jobFromDb.Message.ShouldBe(JsonSerializer.Serialize(jobRequest));

        var logs = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Timestamp)
            .ToListAsync();
        logs.ShouldHaveSingleItem();
        logs.ShouldContain(l => l.EventType == "Created");
    }

    [Fact]
    public async Task GetAndProcessJob_ProcessCreatedJob_ShouldBeCompleted()
    {
        var context = CreateContext();

        var testLogId = await CreateLogInDb(context);

        var jobId = await CreateProcessLogJob(context, testLogId);

        await ProcessJob();

        var jobFromDb = await GetJob(jobId);
        var logFromDb = await GetTestLog(context, testLogId);
        jobFromDb.CurrentState.ShouldBe(State.Completed);

        var logs = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Timestamp)
            .ToListAsync();
        logs.ShouldContain(l => l.EventType == "Created");
        logs.ShouldContain(l => l.EventType == "Processing");
        logs.ShouldContain(l => l.EventType == "Completed");
        logFromDb.ProcessedTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetAndProcessJob_JobThrowsException_ShouldBeFailed()
    {
        var context = CreateContext();

        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var jobFromDb = await GetJob(jobId);

        jobFromDb.CurrentState.ShouldBe(State.Failed);

        var logs = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Timestamp)
            .ToListAsync();
        logs.ShouldContain(l => l.EventType == "Created");
        logs.ShouldContain(l => l.EventType == "Processing");
        logs.ShouldContain(l => l.EventType == "Failed");
    }

    [Fact]
    public async Task GetAndProcessJob_JobWithCounter_CounterShouldBeOne()
    {
        await CreateCounterJob();

        List<Task> tasks = [];
        for (var i = 0; i < 20; i++)
        {
            tasks.Add(ProcessJob());
        }

        await Task.WhenAll([.. tasks]);

        var counter = await GetCounter();
        counter.ShouldBe(1);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenJobWithParentIdIsCreated_ThenChildJobStateShouldBeAwaiting()
    {
        var context = CreateContext();

        var testLogId = await CreateLogInDb(context);

        var jobId = await CreateProcessLogJob(context, testLogId);

        var childJobId = await CreateJobWithParentId(context, jobId);

        await context.SaveChangesAsync();

        var childJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == childJobId)
            .FirstAsync();

        childJob.CurrentState.ShouldBe(State.Awaiting);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenBatchJobIsBeingUpdated_ThenFirstBatchCounterShouldBeUpdatedWhileSecondBatchIsNot()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 10);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 10, firstPlaceholderJobId);

        await context.SaveChangesAsync();

        await ProcessJob();

        var firstBatch = await CreateContext().Set<Batch>()
            .Where(x => x.Id == firstPlaceholderJobId)
            .FirstAsync();

        var secondBatch = await CreateContext().Set<Batch>()
            .Where(x => x.Id == secondPlaceholderJobId)
            .FirstAsync();

        firstBatch.JobCount.ShouldBe(9);
        secondBatch.JobCount.ShouldBe(10);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenAllJobsInFirstBatchAreFinished_ThenFirstBatchCounterShouldBeZeroWhileSecondBatchHasNotChanged()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, firstPlaceholderJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();

        var firstBatch = await CreateContext().Set<Batch>()
            .Where(x => x.Id == firstPlaceholderJobId)
            .FirstAsync();

        var secondBatch = await CreateContext().Set<Batch>()
            .Where(x => x.Id == secondPlaceholderJobId)
            .FirstAsync();

        firstBatch.JobCount.ShouldBe(0);
        secondBatch.JobCount.ShouldBe(2);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenAllJobsInFirstBatchAreFinished_ThenFirstPlaceholderJobStatusShouldBeUpdatedToCompleted()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        _ = await ContinueBatchWith(context, 2, firstPlaceholderJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();

        var firstPlaceholderJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == firstPlaceholderJobId)
            .FirstAsync();

        firstPlaceholderJob.BatchId.ShouldBeNull();
        firstPlaceholderJob.ParentJobId.ShouldBeNull();
        firstPlaceholderJob.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenAllJobsInFirstBatchAreFinished_ThenCUrrentBatchJobStatusShouldBeUpdatedToCompleted()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        _ = await ContinueBatchWith(context, 2, firstPlaceholderJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();

        var currentBatchJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == firstPlaceholderJobId)
            .FirstAsync();

        currentBatchJob.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenAllJobsInFirstBatchAreFinished_ThenAllJobsCurrentStateInSecondBatchShouldBeUpdatedToEnqueued()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, firstPlaceholderJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();

        var secondBatchJobs = await CreateContext().Set<Batch>()
            .Where(x => x.Id == secondPlaceholderJobId)
            .Select(x => x.Jobs)
            .FirstAsync();

        foreach (var batchJob in secondBatchJobs)
        {
            batchJob.CurrentState.ShouldBe(State.Enqueued);
        }
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenAllJobsInSecondBatchAreFinished_ThenAllStatesShouldBeUpdatedToCompleted()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, firstPlaceholderJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();
        await ProcessJob();
        await ProcessJob();

        var firstPlaceholderJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == firstPlaceholderJobId)
            .FirstAsync();

        firstPlaceholderJob.CurrentState.ShouldBe(State.Completed);

        var firstBatch = await CreateContext().Set<Batch>()
            .Where(x => x.Id == firstPlaceholderJobId)
            .FirstAsync();

        var firstBatchJobs = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == firstBatch.Id)
            .ToListAsync();

        foreach (var batchJob in firstBatchJobs)
        {
            batchJob.CurrentState.ShouldBe(State.Completed);
        }

        var secondPlaceholderJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == secondPlaceholderJobId)
            .FirstAsync();

        secondPlaceholderJob.CurrentState.ShouldBe(State.Completed);

        var secondBatch = await CreateContext().Set<Batch>()
            .Where(x => x.Id == secondPlaceholderJobId)
            .FirstAsync();

        var secondBatchJobs = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == secondBatch.Id)
            .ToListAsync();

        foreach (var batchJob in secondBatchJobs)
        {
            batchJob.CurrentState.ShouldBe(State.Completed);
        }
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenFirstBatchJobHasSingleJobAsNextJobToProcess_ThenSingleJobShouldBeCreated()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var singleJobId = await CreateJobWithParentId(context, firstPlaceholderJobId);

        _ = await ContinueBatchWith(context, 2, singleJobId);

        await context.SaveChangesAsync();

        var singleJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == singleJobId)
            .FirstAsync();

        singleJob.ParentJobId.ShouldBe(firstPlaceholderJobId);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenFirstBatchJobHasSingleJobAsNextJobToProcess_ThenSecondBatchPlaceholderJobParentIdEqualsSingleJobId()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var singleJobId = await CreateJobWithParentId(context, firstPlaceholderJobId);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, singleJobId);

        await context.SaveChangesAsync();

        var secondPlaceholderJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == secondPlaceholderJobId)
            .FirstAsync();

        secondPlaceholderJob.ParentJobId.ShouldBe(singleJobId);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenFirstBatchJobsAndSingleJobHaveFinished_ThenSingleJobStatusShouldBeUpdatedToCompleted()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var singleJobId = await CreateJobWithParentId(context, firstPlaceholderJobId);

        _ = await ContinueBatchWith(context, 2, singleJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();
        await ProcessJob();

        var singleJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == singleJobId)
            .FirstAsync();

        singleJob.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenFirstBatchJobsAndSingleJobHaveFinished_ThenSecondPlaceholderJobStateIsNotChanged()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var singleJobId = await CreateJobWithParentId(context, firstPlaceholderJobId);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, singleJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();
        await ProcessJob();

        var secondPlaceholderJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == secondPlaceholderJobId)
            .FirstAsync();

        secondPlaceholderJob.CurrentState.ShouldBe(State.Awaiting);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenFirstBatchJobsAndSingleJobHaveFinished_ThenSecondBatchCounterIsNotChanged()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var singleJobId = await CreateJobWithParentId(context, firstPlaceholderJobId);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, singleJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();
        await ProcessJob();

        var secondBatch = await CreateContext().Set<Batch>()
            .Where(x => x.Id == secondPlaceholderJobId)
            .FirstAsync();

        secondBatch.JobCount.ShouldBe(2);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenFirstBatchJobsAndSingleJobHaveFinished_ThenSecondBatchJobsStateAreChangedToEnqueued()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var singleJobId = await CreateJobWithParentId(context, firstPlaceholderJobId);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, singleJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();
        await ProcessJob();

        var secondBatchJobs = await CreateContext().Set<Batch>()
            .Where(x => x.Id == secondPlaceholderJobId)
            .Select(x => x.Jobs)
            .FirstAsync();

        foreach (var batchJob in secondBatchJobs)
        {
            batchJob.CurrentState.ShouldBe(State.Enqueued);
        }
    }
}
