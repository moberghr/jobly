using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Cronos;
using Handfire.Core;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Tests.TestData.Handlers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Handfire.Tests;
public abstract class TestBase
{
    private IServiceScopeFactory _serviceScopeFactory;

    public TestBase()
    {
        var services = new ServiceCollection();
        var provider = services.AddMediatR(typeof(TestBase))
            .AddTransient<TestContext>(x => CreateContext())
            .AddHandfire<TestContext>(0)
            .BuildServiceProvider();

        _serviceScopeFactory = provider.GetService<IServiceScopeFactory>()!;
    }

    protected abstract TestContext CreateContext();

    protected async Task<string> CreateJob(TestContext context, int testLogId)
    {
        using var transaction = await context.Database.BeginTransactionAsync();

        var publisher = new Publisher<TestContext>(context);
        var processLogJob = new PrecessLogRequest { TestTaskId = testLogId };
        var jobName = Guid.NewGuid().ToString();
        await publisher.Publish(jobName, processLogJob);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();

        return jobName;
    }

    protected async Task<int> CreateLogInDb(TestContext context)
    {
        var logInDb = new TestLog();
        await context.TestLogs.AddAsync(logInDb);
        await context.SaveChangesAsync();

        return logInDb.Id;
    }

    protected async Task<Job> GetJobWithStatesByName(TestContext context, string jobName)
    {
        var job = await context.Set<Job>()
            .Where(x => x.Name == jobName)
            .Include(x => x.JobStates)
            .AsNoTracking()
            .SingleAsync();

        return job;
    }

    protected async Task<TestLog> GetTestLog(TestContext context, int testLogId)
    {
        var log = await context.TestLogs
            .Where(x => x.Id == testLogId)
            .AsNoTracking()
            .SingleAsync();

        return log;
    }

    protected async Task ProcessJob()
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var worker = new HandfireWorkerService<TestContext>(_serviceScopeFactory, new NullLogger<HandfireWorkerService<TestContext>>());

        await worker.GetAndProcessJob(CancellationToken.None);
    }

}
