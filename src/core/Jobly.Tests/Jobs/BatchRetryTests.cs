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
    public async Task GivenBatchWithRetryingJob_WhenJobFailsAndRetries_ThenBatchCounterOnlyDecrementsOnTerminalState()
    {
        await EnsureServerRegistered();
        var context = CreateContext();

        var requests = new List<ThrowExceptionRequest> { new(), new() };
        var batchPublisher = TestUtils.CreateBatchPublisher(context);
        var batchId = await batchPublisher.StartNew(requests);
        await context.SaveChangesAsync();

        // Give BOTH jobs MaxRetries=1 so they retry on first failure
        await CreateContext().Set<Job>()
            .Where(x => x.BatchId == batchId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.MaxRetries, 1));

        var batchBefore = await CreateContext().Set<Batch>()
            .Where(x => x.Id == batchId)
            .FirstAsync();
        batchBefore.JobCount.ShouldBe(2);

        // Process one job — it fails but retries (goes back to Enqueued)
        await ProcessJob();

        var processedJob = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == batchId && x.RetriedTimes == 1)
            .FirstOrDefaultAsync();
        processedJob.ShouldNotBeNull();
        processedJob.CurrentState.ShouldBe(State.Enqueued);

        var batchAfterRetry = await CreateContext().Set<Batch>()
            .Where(x => x.Id == batchId)
            .FirstAsync();
        batchAfterRetry.JobCount.ShouldBe(2); // Not decremented — job is retrying, not finished
    }

    [Fact]
    public async Task GivenBatchWithDefaultOptions_WhenAllJobsSucceed_ThenContinuationFires()
    {
        await EnsureServerRegistered();
        var context = CreateContext();

        var batchId = await CreateBatch(context, 2);
        var continuationId = await ContinueBatchWith(context, 1, batchId);
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        var batchJob = await GetJob(batchId);
        batchJob.CurrentState.ShouldBe(State.Completed);

        // Continuation batch jobs should have been enqueued and completed
        var continuationJobs = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == continuationId)
            .ToListAsync();
        continuationJobs.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [Fact]
    public async Task GivenBatchWithDefaultOptions_WhenAnyJobFails_ThenContinuationDoesNotFire()
    {
        await EnsureServerRegistered();
        var context = CreateContext();

        // Create batch of 2 jobs — one will fail
        var requests = new List<ThrowExceptionRequest> { new(), new() };
        var batchPublisher = TestUtils.CreateBatchPublisher(context);
        var batchId = await batchPublisher.StartNew(requests);

        // Create continuation
        var continuationRequests = new List<UnitRequest> { new() };
        var continuationId = await batchPublisher.ContinueBatchWith(continuationRequests, batchId);
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        // Batch placeholder should be Failed (not Completed)
        var batchJob = await GetJob(batchId);
        batchJob.CurrentState.ShouldBe(State.Failed);

        // Continuation should remain Awaiting
        var continuationBatchJob = await GetJob(continuationId);
        continuationBatchJob.CurrentState.ShouldBe(State.Awaiting);

        var continuationJobs = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == continuationId)
            .ToListAsync();
        continuationJobs.ShouldAllBe(j => j.CurrentState == State.Awaiting);
    }

    [Fact]
    public async Task GivenBatchWithOnAnyFinished_WhenSomeJobsFail_ThenContinuationStillFires()
    {
        await EnsureServerRegistered();
        var context = CreateContext();

        // Create batch with OnAnyFinishedState
        var requests = new List<ThrowExceptionRequest> { new(), new() };
        var batchPublisher = TestUtils.CreateBatchPublisher(context);
        var batchId = await batchPublisher.StartNew(requests, BatchContinuationOptions.OnAnyFinishedState);

        // Create continuation
        var continuationRequests = new List<UnitRequest> { new() };
        var continuationId = await batchPublisher.ContinueBatchWith(continuationRequests, batchId);
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        // Batch placeholder should be Completed (OnAnyFinishedState ignores failures)
        var batchJob = await GetJob(batchId);
        batchJob.CurrentState.ShouldBe(State.Completed);

        // Continuation should have been enqueued and completed
        var continuationJobs = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == continuationId)
            .ToListAsync();
        continuationJobs.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [Fact]
    public async Task GivenBatchWithOnAnyFinished_WhenAllJobsSucceed_ThenContinuationFires()
    {
        await EnsureServerRegistered();
        var context = CreateContext();

        var batchId = await CreateBatchWithOptions(context, 2, BatchContinuationOptions.OnAnyFinishedState);
        var continuationId = await ContinueBatchWith(context, 1, batchId);
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        var batchJob = await GetJob(batchId);
        batchJob.CurrentState.ShouldBe(State.Completed);

        var continuationJobs = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == continuationId)
            .ToListAsync();
        continuationJobs.ShouldAllBe(j => j.CurrentState == State.Completed);
    }
}
