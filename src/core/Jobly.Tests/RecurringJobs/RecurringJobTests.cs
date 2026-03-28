using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    private readonly string _cronExpression = "* * * * *";

    [Fact]
    public async Task AddOrUpdateRecurringJob_AddRecurringJob_ShouldBeInDb()
    {
        // CREATE new RecurringJob entity
        var recurringJobName = await CreateUnitRecurringJob(_cronExpression);

        // get created RecurringJob entity
        var recurringJobEntity = await GetRecurringJob(recurringJobName);

        // get created Job entity
        var nextJobId = recurringJobEntity.NextJobId!;
        var nextJob = await GetJob(nextJobId.Value);

        // check RecurringJob
        recurringJobEntity.ShouldNotBeNull();
        recurringJobEntity.Cron.ShouldBe(_cronExpression);
        recurringJobEntity.Name.ShouldBe(recurringJobName);

        // check Job
        nextJob.ShouldNotBeNull();
        recurringJobEntity.NextExecution.ShouldBe(nextJob.ScheduleTime);
    }

    [Fact]
    public async Task SaveChangesAsync_UpdateWhenEntityHasNewConcurrencyToken_ShouldThrowDbUpdateConcurrencyException()
    {
        // get contexts
        var context1 = CreateContext();
        var context2 = CreateContext();

        // create entity
        var name = await CreateUnitRecurringJob(_cronExpression);
        await context1.SaveChangesAsync();

        // get entity
        var version1 = await context1.Set<RecurringJob>().Where(x => x.Name == name).SingleAsync();
        var version2 = await context2.Set<RecurringJob>().Where(x => x.Name == name).SingleAsync();

        // update entity
        version1.Message = "new message 1";
        version2.Message = "new message 2";

        await context1.SaveChangesAsync();
        context2.SaveChangesAsync().ShouldThrow<DbUpdateConcurrencyException>();
    }
}
