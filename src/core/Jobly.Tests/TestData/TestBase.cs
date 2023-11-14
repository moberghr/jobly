using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jobly.Tests;
public abstract class TestBase
{
    private IServiceScopeFactory _serviceScopeFactory;
    private IServiceScopeFactory _serviceScopeFactoryNoLocking;

    public TestBase()
    {
        var services = new ServiceCollection();
        var provider = services.AddMediatR(typeof(TestBase))
            .AddTransient<TestContext>(x => CreateContext())
            .AddJobly<TestContext>(0)
            .AddJoblyWorker<TestContext>(0)
            .AddSingleton<CounterService>()
            .BuildServiceProvider();

        _serviceScopeFactory = provider.GetService<IServiceScopeFactory>()!;

        var providerWithNoLocking = services.AddMediatR(typeof(TestBase))
            .AddTransient<TestContext>(x => CreateContextWithoutJobLocking())
            .AddJobly<TestContext>(0)
            .AddJoblyWorker<TestContext>(0)
            .AddSingleton<CounterService>()
            .BuildServiceProvider();

        _serviceScopeFactoryNoLocking = providerWithNoLocking.GetService<IServiceScopeFactory>()!;
    }

    protected abstract TestContext CreateContext();

    protected abstract TestContext CreateContextWithoutJobLocking();

    protected async Task<string> CreateProcessLogJob(TestContext context, int testLogId)
    {
        var publisher = new Publisher<TestContext>(context, 0);
        var processLogJob = new PrecessLogRequest { TestTaskId = testLogId };
        var jobId = await publisher.Publish(processLogJob);

        await context.SaveChangesAsync();

        return jobId;
    }

    protected async Task<string> CreateFailedJob(TestContext context)
    {
        var publisher = new Publisher<TestContext>(context, 0);

        var throwExceptionRequest = new ThrowExceptionRequest();

        var jobId = await publisher.Publish(throwExceptionRequest);

        await context.SaveChangesAsync();

        return jobId;
    }

    protected async Task<string> CreateFailedRetryJob(TestContext context, int retries, int? maxRetries, string? parentJobId)
    {
        var publisher = new Publisher<TestContext>(context, retries);

        var throwExceptionRequest = new ThrowExceptionRequest();
        string jobId = "";
        if (maxRetries != null)
        {
            jobId = await publisher.Publish(throwExceptionRequest, (int)maxRetries);
        }

        if (!string.IsNullOrEmpty(parentJobId))
        {
            jobId = await publisher.Publish(throwExceptionRequest, parentJobId);
        }

        if (maxRetries == null && string.IsNullOrEmpty(parentJobId))
        {
            jobId = await publisher.Publish(throwExceptionRequest);
        }

        await context.SaveChangesAsync();

        return jobId;
    }

    protected async Task ChangeJobFromException(string jobId)
    {
        var jobRequest = new UnitRequest();
        var context = CreateContext();
        var currentJob = await GetJob(jobId);
        currentJob.Type = jobRequest.GetType().AssemblyQualifiedName!;

        context.Set<Job>().Update(currentJob);

        await context.SaveChangesAsync();
    }

    protected async Task CreateCounterJob()
    {
        var context = CreateContext();

        var publisher = new Publisher<TestContext>(context, 0);

        var request = new CounterRequest();

        await publisher.Publish(request);

        await context.SaveChangesAsync();
    }

    protected async Task<int> CreateLogInDb(TestContext context)
    {
        var logInDb = new TestLog();
        await context.TestLogs.AddAsync(logInDb);
        await context.SaveChangesAsync();

        return logInDb.Id;
    }

    protected async Task<string> CreateJobWithParentId(TestContext context, string parentJobId)
    {
        var requests = new UnitRequest();

        var publisher = new Publisher<TestContext>(context, 0);

        var jobId = await publisher.Publish(requests, parentJobId);

        return jobId;
    }

    protected async Task<string> CreateBatch(TestContext context, int numberOfJobs)
    {
        var requests = new List<UnitRequest>();

        for (int i = 0; i < numberOfJobs; i++)
        {
            var request = new UnitRequest();

            requests.Add(request);
        }

        var batchPublisher = new BatchPublisher<TestContext>(context);

        var placeholderJobId = await batchPublisher.StartNew(requests);

        return placeholderJobId;
    }

    protected async Task<string> ContinueBatchWith(TestContext context, int numberOfJobs, string placeholderJobId)
    {
        var requests = new List<UnitRequest>();

        for (int i = 0; i < numberOfJobs; i++)
        {
            var request = new UnitRequest();

            requests.Add(request);
        }

        var batchPublisher = new BatchPublisher<TestContext>(context);

        var newPlaceholderJobId = await batchPublisher.ContinueBatchWith(requests, placeholderJobId);

        return newPlaceholderJobId;
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

        var worker = new JoblyWorkerService<TestContext>(_serviceScopeFactory, new NullLogger<JoblyWorkerService<TestContext>>());

        await worker.GetAndProcessJob(CancellationToken.None);

        return;
    }

    protected async Task ProcessJobWithoutLocking()
    {
        using var scope = _serviceScopeFactoryNoLocking.CreateScope();

        var worker = new JoblyWorkerService<TestContext>(_serviceScopeFactoryNoLocking, new NullLogger<JoblyWorkerService<TestContext>>());

        await worker.GetAndProcessJob(CancellationToken.None);
    }

    protected async Task<int> GetCounterForNoLocking()
    {
        using var scope = _serviceScopeFactoryNoLocking.CreateScope();

        var counterService = scope.ServiceProvider.GetService<CounterService>();

        return counterService.Counter;
    }

    protected async Task<int> GetCounter()
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var counterService = scope.ServiceProvider.GetService<CounterService>();

        return counterService.Counter;
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
