using Handfire.Core.Enums;
using Handfire.Core;
using Handfire.Tests.TestData.Handlers;
using System.Text.Json;
using Shouldly;
using Handfire.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Handfire.Core.Entities;

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
    public async Task GivenGetAndProcessJob_WhenBatchJobIsBeingUpdated_ThenFirstBatchCounterShouldBeUpdatedWhileSecondBatchIsNot()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 10);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 10, firstPlaceholderJobId);

        await context.SaveChangesAsync();

        await ProcessJob();

        var firstBatch = await CreateContext().Set<Batch>()
            .Where(x => x.JobId == firstPlaceholderJobId)
            .FirstAsync();

        var secondBatch = await CreateContext().Set<Batch>()
            .Where(x => x.JobId == secondPlaceholderJobId)
            .FirstAsync();

        firstBatch.Counter.ShouldBe(9);
        secondBatch.Counter.ShouldBe(10);
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
            .Where(x => x.JobId == firstPlaceholderJobId)
            .FirstAsync();

        var secondBatch = await CreateContext().Set<Batch>()
            .Where(x => x.JobId == secondPlaceholderJobId)
            .FirstAsync();

        firstBatch.Counter.ShouldBe(0);
        secondBatch.Counter.ShouldBe(2);
    }

    [Fact]
    public async Task GivenGetAndProcessJob_WhenAllJobsInFirstBatchAreFinished_ThenFirstPlaceholderJobStatusShouldBeUpdatedToCompleted()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, firstPlaceholderJobId);

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
    public async Task GivenGetAndProcessJob_WhenAllJobsInFirstBatchAreFinished_ThenFirstBatchStatusShouldBeUpdatedToCompleted()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, firstPlaceholderJobId);

        await context.SaveChangesAsync();

        await ProcessJob();
        await ProcessJob();

        var firstBatch = await CreateContext().Set<Batch>()
            .Where(x => x.JobId == firstPlaceholderJobId)
            .FirstAsync();

        firstBatch.BatchStatus.ShouldBe(State.Completed);
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
            .Where(x => x.JobId == secondPlaceholderJobId)
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
            .Where(x => x.JobId == firstPlaceholderJobId)
            .FirstAsync();

        firstBatch.BatchStatus.ShouldBe(State.Completed);

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
            .Where(x => x.JobId == secondPlaceholderJobId)
            .FirstAsync();

        secondBatch.BatchStatus.ShouldBe(State.Completed);

        var secondBatchJobs = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == secondBatch.Id)
            .ToListAsync();

        foreach (var batchJob in secondBatchJobs)
        {
            batchJob.CurrentState.ShouldBe(State.Completed);
        }
    }
}

