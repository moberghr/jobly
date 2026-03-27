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
    public async Task GivenCompletedJob_WhenProcessed_ThenExpireAtIsSet()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
        job.ExpireAt.ShouldNotBeNull();
        job.ExpireAt.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task GivenFailedJob_WhenProcessed_ThenExpireAtIsNull()
    {
        var context = CreateContext();

        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Failed);
        job.ExpireAt.ShouldBeNull();
    }

    [Fact]
    public async Task GivenCompletedJob_WhenProcessed_ThenSucceededStatIncremented()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var statBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var statAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        statAfter.ShouldBe(statBefore + 1);
    }

    [Fact]
    public async Task GivenFailedJob_WhenRetriesExhausted_ThenFailedStatIncremented()
    {
        var context = CreateContext();

        var statBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var statAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        statAfter.ShouldBe(statBefore + 1);
    }

    [Fact]
    public async Task GivenDeletedJob_ThenDeletedStatIncremented()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var statBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var service = TestUtils.CreateJoblyService(CreateContext());
        await service.DeleteJob(jobId);

        var statAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        statAfter.ShouldBe(statBefore + 1);
    }

    [Fact]
    public async Task GivenDashboardStats_ThenHistoricalTotalsIncluded()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var service = TestUtils.CreateJoblyService(CreateContext());
        var stats = await service.GetJoblyStatus();

        stats.TotalSucceeded.ShouldBeGreaterThanOrEqualTo(1);
    }
}
