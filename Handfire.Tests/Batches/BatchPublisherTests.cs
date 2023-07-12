using Handfire.Core.Data.Entities;
using Handfire.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Handfire.Tests.Jobs;

public abstract partial class HandfireTests : TestBase
{
    [Fact]
    public async Task GivenAddBatchAndBatchContinuationJobs_WhenNewBatchIsCreated_ThenNewBatchIsCreated()
    {
        await CreateBatch(10);

        var newBatchData = await CreateContext().Set<Batch>()
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
    }

    [Fact]
    public async Task GivenAddBatchAndBatchContinuationJobs_WhenNewBatchIsCreated_ThenBatchJobsStateShouldBeEnqueued()
    {
        await CreateBatch(10);

        var batchJobs = await CreateContext().Set<Batch>()
            .Select(x => x.Jobs)
            .FirstAsync();

        foreach (var batchJob in batchJobs)
        {
            batchJob.CurrentState.ShouldBe(State.Enqueued);
        }
    }

    [Fact]
    public async Task GivenAddBatchAndBatchContinuationJobs_WhenNewBatchIsCreated_ThenNewBatchContinuationIsCreated()
    {
        await CreateBatch(10);

        var newBatchContinuations = await CreateContext().Set<BatchContinuation>()
            .ToListAsync();

        newBatchContinuations.Count.ShouldBe(10);
    }

    [Fact]
    public async Task GivenAddBatchAndBatchContinuationJobs_WhenNewBatchIsCreated_ThenBatchContinuationJobsStateShouldBeAwaiting()
    {
        await CreateBatch(10);

        var batchContinuationJobStates = await CreateContext().Set<BatchContinuation>()
            .Select(x => x.Job.CurrentState)
            .ToListAsync();

        foreach (var batchContinuationJobState in batchContinuationJobStates)
        {
            batchContinuationJobState.ShouldBe(State.Awaiting);
        }
    }
}
