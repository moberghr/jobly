using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenCreateBatchJobs_WhenNewBatchAndContinuationBatchAreCreated_ThenBothBatchesMustBeInDb()
    {
        var context = CreateContext();

        var placeholderJobId = await CreateBatch(context, 10);

        await ContinueBatchWith(context, 10, placeholderJobId);

        await context.SaveChangesAsync();

        var newBatches = await CreateContext().Set<Batch>()
            .ToListAsync();

        newBatches.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenCreateBatchJobs_WhenNewBatchAndContinuationBatchAreCreated_ThenCounterOnBothShouldBe10()
    {
        var context = CreateContext();

        var placeholderJobId = await CreateBatch(context, 10);

        await ContinueBatchWith(context, 10, placeholderJobId);

        await context.SaveChangesAsync();

        var newBatches = await CreateContext().Set<Batch>()
            .ToListAsync();

        foreach (var newBatch in newBatches)
        {
            newBatch.Counter.ShouldBe(10);
        }
    }

    [Fact]
    public async Task GivenCreateBatchJobs_WhenNewBatchAndContinuationBatchAreCreated_ThenPlaceholdedJobIdMustbeJobIdInBatchTable()
    {
        var context = CreateContext();

        var firstPlaceholderJobId = await CreateBatch(context, 2);

        var secondPlaceholderJobId = await ContinueBatchWith(context, 2, firstPlaceholderJobId);

        await context.SaveChangesAsync();

        var firstPlaceholderJob = await CreateContext().Set<Job>()
            .Where(x => x.Id == firstPlaceholderJobId)
            .FirstOrDefaultAsync();

        firstPlaceholderJob.ShouldNotBeNull();
        firstPlaceholderJob.CurrentState.ShouldBe(State.Awaiting);

        var firstBatch = await CreateContext().Set<Batch>()
            .Where(x => x.Id == firstPlaceholderJob.Id)
            .FirstOrDefaultAsync();

        firstBatch.ShouldNotBeNull();

        var secondPlaceholderJob = await CreateContext().Set<Job>()
            .Where(x => x.ParentJobId == firstBatch.Id)
            .FirstOrDefaultAsync();

        secondPlaceholderJob.ShouldNotBeNull();
        secondPlaceholderJob.CurrentState.ShouldBe(State.Awaiting);
        secondPlaceholderJob.Id.ShouldBe(secondPlaceholderJobId);

        var secondBatch = await CreateContext().Set<Batch>()
            .Where(x => x.Id == secondPlaceholderJob.Id)
            .FirstOrDefaultAsync();

        secondBatch.ShouldNotBeNull();
    }
}
