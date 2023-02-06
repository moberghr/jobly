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

    protected async Task<string> CreateProcessLogJob(TestContext context, int testLogId)
    {
        var publisher = new Publisher<TestContext>(context);
        var processLogJob = new PrecessLogRequest { TestTaskId = testLogId };
        var jobId = await publisher.Publish(processLogJob);

        await context.SaveChangesAsync();

        return jobId;
    }

    protected async Task<string> CreateFailedJob(TestContext context)
    {
        var publisher = new Publisher<TestContext>(context);

        var throwExceptionRequest = new ThrowExceptionRequest();

        var jobId = await publisher.Publish(throwExceptionRequest);

        await context.SaveChangesAsync();

        return jobId;
    }

    protected async Task<int> CreateLogInDb(TestContext context)
    {
        var logInDb = new TestLog();
        await context.TestLogs.AddAsync(logInDb);
        await context.SaveChangesAsync();

        return logInDb.Id;
    }

    protected async Task<Job> GetJobWithStates(TestContext context, string jobId)
    {
        var job = await context.Set<Job>()
            .Where(x => x.Id == jobId)
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

    protected async Task<string> CreateUnitRecurringJob(string cronExpression)
    {
        var name = Guid.NewGuid().ToString();

        var context = CreateContext();

        var publisher = new RecurringJobPublisher<TestContext>(context);
        var request = new UnitRequest();
        await publisher.AddOrUpdateRecurringJob(request, name, cronExpression);

        return name;
    }
    protected async Task<RecurringJob> GetRecurringJob(string name)
    {
        var context = CreateContext();

        var recurringJob = await context.Set<RecurringJob>()
            .Where(x => x.Name == name)
            .SingleOrDefaultAsync();

        return recurringJob;
    }

    protected async Task<Job> GetJob(string id)
    {
        var context = CreateContext();

        var job = await context.Set<Job>()
            .Where(x => x.Id == id)
            .SingleOrDefaultAsync();

        return job;
    }
}
