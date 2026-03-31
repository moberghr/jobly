using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenBatchDependingOnFailedJob_WhenParentFails_ThenBatchJobsRemainAwaiting()
    {
        var context = CreateContext();

        var parentJobId = await CreateFailedJob(context);

        var batchPlaceholderJobId = await ContinueBatchWith(context, 2, parentJobId);

        await context.SaveChangesAsync();

        await ProcessJob();

        var parentJob = await GetJob(parentJobId);
        parentJob.CurrentState.ShouldBe(State.Failed);

        var batchPlaceholderJob = await GetJob(batchPlaceholderJobId);
        batchPlaceholderJob.CurrentState.ShouldBe(State.Awaiting);

        var batchJobs = await CreateContext().Set<Job>()
            .Where(x => x.ParentJobId == batchPlaceholderJobId && x.Kind == JobKind.Job)
            .ToListAsync();

        batchJobs.ShouldNotBeEmpty();
        foreach (var batchJob in batchJobs)
        {
            batchJob.CurrentState.ShouldBe(State.Awaiting);
        }
    }
}
