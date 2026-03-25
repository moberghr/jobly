using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenRecurringJob_WhenProcessed_ThenNextJobIsCreated()
    {
        var name = await CreateUnitRecurringJob("* * * * *");

        var recurringJob = await GetRecurringJob(name);
        var originalNextJobId = recurringJob.NextJobId!.Value;

        // Make the next job eligible for processing by setting ScheduleTime to past
        var context = CreateContext();
        var nextJob = await context.Set<Job>().FindAsync(originalNextJobId);
        nextJob!.ScheduleTime = DateTime.UtcNow.AddSeconds(-1);
        await context.SaveChangesAsync();

        await ProcessJob();

        var processedJob = await GetJob(originalNextJobId);
        processedJob.CurrentState.ShouldBe(Jobly.Core.Enums.State.Completed);

        var updatedRecurringJob = await GetRecurringJob(name);
        updatedRecurringJob.NextJobId.ShouldNotBe(originalNextJobId);
        updatedRecurringJob.LastJobId.ShouldBe(originalNextJobId);
    }

    [Fact]
    public async Task GivenRecurringJob_WhenProcessed_ThenLastExecutionIsUpdated()
    {
        var name = await CreateUnitRecurringJob("* * * * *");

        var recurringJob = await GetRecurringJob(name);
        var originalNextExecution = recurringJob.NextExecution;
        var originalNextJobId = recurringJob.NextJobId!.Value;

        // Make the next job eligible for processing
        var context = CreateContext();
        var nextJob = await context.Set<Job>().FindAsync(originalNextJobId);
        nextJob!.ScheduleTime = DateTime.UtcNow.AddSeconds(-1);
        await context.SaveChangesAsync();

        await ProcessJob();

        var updatedRecurringJob = await GetRecurringJob(name);
        updatedRecurringJob.LastExecution.ShouldBe(originalNextExecution);
    }
}
