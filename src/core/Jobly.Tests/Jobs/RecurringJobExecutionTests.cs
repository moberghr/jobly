using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Worker.Services;
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

        // Make the next job and recurring job's NextExecution eligible by setting to past
        var context = CreateContext();
        var nextJob = await context.Set<Job>().FindAsync(originalNextJobId);
        nextJob!.ScheduleTime = DateTime.UtcNow.AddSeconds(-1);
        var rj = await context.Set<RecurringJob>().FindAsync(recurringJob.Id);
        rj!.NextExecution = DateTime.UtcNow.AddSeconds(-1);
        await context.SaveChangesAsync();

        await ProcessJob();

        // Run the recurring job scheduler to create the next occurrence
        await RecurringJobSchedulerTask<TestContext>.ScheduleRecurringJobs<TestContext>(CreateContext());

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
        var originalNextJobId = recurringJob.NextJobId!.Value;

        // Make the next job and recurring job's NextExecution eligible by setting to past
        var pastTime = DateTime.UtcNow.AddSeconds(-1);
        var context = CreateContext();
        var nextJob = await context.Set<Job>().FindAsync(originalNextJobId);
        nextJob!.ScheduleTime = pastTime;
        var rj = await context.Set<RecurringJob>().FindAsync(recurringJob.Id);
        rj!.NextExecution = pastTime;
        await context.SaveChangesAsync();

        await ProcessJob();

        // Run the recurring job scheduler to update the recurring job state
        await RecurringJobSchedulerTask<TestContext>.ScheduleRecurringJobs<TestContext>(CreateContext());

        var updatedRecurringJob = await GetRecurringJob(name);
        updatedRecurringJob.LastExecution.ShouldNotBeNull();
        (updatedRecurringJob.LastExecution.Value - pastTime).Duration().TotalMilliseconds.ShouldBeLessThan(1);
    }
}
