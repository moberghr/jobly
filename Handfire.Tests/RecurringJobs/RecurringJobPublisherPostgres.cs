using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Handfire.Core;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Tests.TestData;
using Handfire.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Tests.RecurringJobs;
public class RecurringJobPublisherPostgres : PostgreSqlTestBase
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
        var nextJob = await GetJob(nextJobId);

        // check RecurringJob
        Assert.NotNull(recurringJobEntity);
        Assert.Equal(_cronExpression, recurringJobEntity.Cron);
        Assert.Equal(recurringJobName, recurringJobEntity.Name);

        // check Job
        Assert.True(nextJob != null);
        Assert.Equal(nextJob.ScheduleTime, recurringJobEntity.NextExecution);
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

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context2.SaveChangesAsync());
    }
}
