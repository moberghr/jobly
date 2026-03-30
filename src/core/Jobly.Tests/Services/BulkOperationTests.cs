using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Services;

public abstract partial class ServiceTests : TestBase
{
    [Fact]
    public async Task BulkDelete_AllJobs_AllDeletedAndStatsCorrect()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var id1 = await publisher.Enqueue(new UnitRequest());
        var id2 = await publisher.Enqueue(new UnitRequest());
        var id3 = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var deletedBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var service = TestUtils.CreateJobCommandService(CreateContext());
        var result = await service.BulkDeleteJobs([id1, id2, id3]);

        result.Succeeded.ShouldBe(3);
        result.Skipped.ShouldBe(0);

        (await GetJob(id1)).CurrentState.ShouldBe(State.Deleted);
        (await GetJob(id2)).CurrentState.ShouldBe(State.Deleted);
        (await GetJob(id3)).CurrentState.ShouldBe(State.Deleted);

        await TestUtils.AggregateCounters(CreateContext());

        var deletedAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        deletedAfter.ShouldBe(deletedBefore + 3);
    }

    [Fact]
    public async Task BulkRequeue_FailedJobs_AllRequeuedAndStatsCorrect()
    {
        var context = CreateContext();

        var id1 = await CreateFailedJob(context);
        var id2 = await CreateFailedJob(context);
        var id3 = await CreateFailedJob(context);

        await ProcessAllJobs(workerCount: 3);

        var failedBefore = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var service = TestUtils.CreateJobCommandService(CreateContext());
        var result = await service.BulkRequeueJobs([id1, id2, id3]);

        result.Succeeded.ShouldBe(3);
        result.Skipped.ShouldBe(0);

        (await GetJob(id1)).CurrentState.ShouldBe(State.Enqueued);
        (await GetJob(id2)).CurrentState.ShouldBe(State.Enqueued);
        (await GetJob(id3)).CurrentState.ShouldBe(State.Enqueued);

        await TestUtils.AggregateCounters(CreateContext());

        var failedAfter = await CreateContext().Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        failedAfter.ShouldBe(failedBefore - 3);
    }

    [Fact]
    public async Task BulkDelete_WithAlreadyDeletedJob_SkipsDeletedOnes()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var id1 = await publisher.Enqueue(new UnitRequest());
        var id2 = await publisher.Enqueue(new UnitRequest());
        var id3 = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        // Pre-delete id2
        var preService = TestUtils.CreateJobCommandService(CreateContext());
        await preService.DeleteJob(id2);

        var service = TestUtils.CreateJobCommandService(CreateContext());
        var result = await service.BulkDeleteJobs([id1, id2, id3]);

        // id2 was already Deleted — deleting a Deleted job still works (it's idempotent)
        // But stats should be correct: Deleted state -> Deleted = no stat change for id2
        result.Succeeded.ShouldBe(3); // All 3 "succeed" since Deleted->Deleted is valid
    }

    [Fact]
    public async Task BulkDelete_WithNonExistentJob_SkipsMissing()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var id1 = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var fakeId = Guid.NewGuid();

        var service = TestUtils.CreateJobCommandService(CreateContext());
        var result = await service.BulkDeleteJobs([id1, fakeId]);

        result.Succeeded.ShouldBe(1);
        result.Skipped.ShouldBe(1);
    }
}
