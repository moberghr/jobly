using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Handfire.Tests.Jobs;

public abstract partial class HandfireTests : TestBase
{
    [Fact]
    public async Task GivenCreateBatchJobs_WhenFirstAndSecondBatchAreCreated_ThenBothBatchesMustBeInDb()
    {
        await CreateBatch(10);

        var newBatches = await CreateContext().Set<Batch>()
            .ToListAsync();

        newBatches.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenCreateBatchJobs_WhenFirstAndSecondBatchAreCreated_ThenCounterOnBothShouldBe10()
    {
        await CreateBatch(10);

        var newBatches = await CreateContext().Set<Batch>()
            .ToListAsync();

        foreach (var newBatch in newBatches)
        {
            newBatch.Counter = 10;
        }
    }

    [Fact]
    public async Task GivenCreateBatchJobs_WhenFirstAndSecondPlaceholderJobIsCreated_ThenPlaceholdedJobIdMustbeJobIdInBatchTable()
    {
        await CreateBatch(2);

        var firstPlaceholderJob = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == null)
            .Where(x => x.ParentJobId == null)
            .FirstOrDefaultAsync();

        firstPlaceholderJob.ShouldNotBeNull();
        firstPlaceholderJob.CurrentState.ShouldBe(State.Awaiting);

        var firstBatch = await CreateContext().Set<Batch>()
            .Where(x => x.JobId == firstPlaceholderJob.Id)
            .FirstOrDefaultAsync();

        firstBatch.ShouldNotBeNull();
        firstBatch.BatchStatus.ShouldBe(State.Enqueued);

        var secondPlaceholderJob = await CreateContext().Set<Job>()
            .Where(x => x.ParentJobId == firstBatch.JobId)
            .FirstOrDefaultAsync();

        secondPlaceholderJob.ShouldNotBeNull();
        secondPlaceholderJob.CurrentState.ShouldBe(State.Awaiting);

        var secondBatch = await CreateContext().Set<Batch>()
            .Where(x => x.JobId == secondPlaceholderJob.Id)
            .FirstOrDefaultAsync();

        secondBatch.ShouldNotBeNull();
        secondBatch.BatchStatus.ShouldBe(State.Awaiting);
    }
}
