using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenMultipleHandlers_WhenJobProcessed_ThenFansOutToSeparateJobs()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // First ProcessJob: routes the original job, creates 2 child jobs, then processes one
        await ProcessJob();
        // Second ProcessJob: processes the other child job
        await ProcessJob();

        var counter = GetMultiHandlerCounter();
        counter.CountA.ShouldBe(1);
        counter.CountB.ShouldBe(1);
    }

    [Fact]
    public async Task GivenMultipleHandlers_WhenJobProcessed_ThenOriginalJobIsCompleted()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // Route phase completes the original job
        await ProcessJob();

        var originalJob = await GetJob(jobId);
        originalJob.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenMultipleHandlers_WhenJobProcessed_ThenChildJobsHaveHandlerTypeSet()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var childJobs = await CreateContext().Set<Job>()
            .Where(j => j.HandlerType != null && j.Type == typeof(MultiRequest).AssemblyQualifiedName)
            .ToListAsync();

        childJobs.Count.ShouldBe(2);
        childJobs.ShouldContain(j => j.HandlerType!.Contains(nameof(MultiHandlerA)));
        childJobs.ShouldContain(j => j.HandlerType!.Contains(nameof(MultiHandlerB)));
    }
}
