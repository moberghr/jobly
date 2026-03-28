using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenCompletedJob_ThenHourlySucceededStatIncremented()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var hourKey = $"stats:succeeded:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        var hourlyStat = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == hourKey)
            .FirstOrDefaultAsync();

        hourlyStat.ShouldNotBeNull();
        hourlyStat.Value.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GivenFailedJob_ThenHourlyFailedStatIncremented()
    {
        var context = CreateContext();
        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var hourKey = $"stats:failed:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        var hourlyStat = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == hourKey)
            .FirstOrDefaultAsync();

        hourlyStat.ShouldNotBeNull();
        hourlyStat.Value.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GivenMultipleCompletedJobs_ThenHourlyStatAccumulates()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Enqueue(new UnitRequest());
        await publisher.Enqueue(new UnitRequest());
        await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessAllJobs(workerCount: 3);

        var hourKey = $"stats:succeeded:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        var hourlyStat = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == hourKey)
            .FirstOrDefaultAsync();

        hourlyStat.ShouldNotBeNull();
        hourlyStat.Value.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GivenStatsHistory_ThenReturnsCorrectHourlyData()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var service = TestUtils.CreateJoblyService(CreateContext());
        var history = await service.GetStatsHistory(24);

        history.Count.ShouldBeGreaterThanOrEqualTo(1);
        history.ShouldContain(p => p.Succeeded >= 1);
    }
}
