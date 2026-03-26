using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobly.Tests;

public abstract class TestBase
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SemaphoreSlim _serverRegistrationLock = new(1, 1);
    private bool _serverRegistered;

    protected TestBase()
    {
        var services = new ServiceCollection();
        var provider = services.AddJobHandlers(typeof(TestBase).Assembly)
            .AddTransient<TestContext>(x => CreateContext())
            .AddJoblyWorker<TestContext>()
            .AddSingleton<CounterService>()
            .AddSingleton<MultiHandlerCounter>()
            .BuildServiceProvider();

        _serviceScopeFactory = provider.GetService<IServiceScopeFactory>()!;
    }

    protected abstract TestContext CreateContext();

    protected async Task EnsureServerRegistered()
    {
        if (_serverRegistered) return;
        await _serverRegistrationLock.WaitAsync();
        try
        {
            if (_serverRegistered) return;
            await TestUtils.RegisterTestServer(CreateContext());
            _serverRegistered = true;
        }
        finally
        {
            _serverRegistrationLock.Release();
        }
    }

    protected static async Task<Guid> CreateProcessLogJob(TestContext context, int testLogId)
    {
        var publisher = TestUtils.CreatePublisher(context);
        var processLogJob = new PrecessLogRequest { TestTaskId = testLogId };
        var jobId = await publisher.Publish(processLogJob);

        await context.SaveChangesAsync();

        return jobId;
    }

    protected async Task<Guid> CreateFailedJob(TestContext context)
    {
        var publisher = TestUtils.CreatePublisher(context);

        var throwExceptionRequest = new ThrowExceptionRequest();

        var jobId = await publisher.Publish(throwExceptionRequest);

        await context.SaveChangesAsync();

        return jobId;
    }

    protected async Task<Guid> CreateFailedRetryJob(TestContext context, int retries, int? maxRetries, Guid? parentJobId)
    {
        var publisher = TestUtils.CreatePublisher(context, retries);

        var throwExceptionRequest = new ThrowExceptionRequest();
        Guid? jobId = null;
        if (maxRetries != null)
        {
            jobId = await publisher.Publish(throwExceptionRequest, (int)maxRetries);
        }

        if (parentJobId != null)
        {
            jobId = await publisher.Publish(throwExceptionRequest, parentJobId.Value);
        }

        if (maxRetries == null && parentJobId == null)
        {
            jobId = await publisher.Publish(throwExceptionRequest);
        }

        await context.SaveChangesAsync();

        return jobId!.Value;
    }
    

    protected async Task ChangeJobFromException(Guid jobId)
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

        var publisher = TestUtils.CreatePublisher(context);

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

    protected async Task<Guid> CreateJobWithParentId(TestContext context, Guid parentJobId)
    {
        var requests = new UnitRequest();

        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Publish(requests, parentJobId);

        return jobId;
    }

    protected async Task<Guid> CreateBatch(TestContext context, int numberOfJobs)
    {
        var requests = new List<UnitRequest>();

        for (var i = 0; i < numberOfJobs; i++)
        {
            var request = new UnitRequest();

            requests.Add(request);
        }

        var batchPublisher = TestUtils.CreateBatchPublisher(context);

        var placeholderJobId = await batchPublisher.StartNew(requests);

        return placeholderJobId;
    }

    protected async Task<Guid> ContinueBatchWith(TestContext context, int numberOfJobs, Guid placeholderJobId)
    {
        var requests = new List<UnitRequest>();

        for (var i = 0; i < numberOfJobs; i++)
        {
            var request = new UnitRequest();

            requests.Add(request);
        }

        var batchPublisher = TestUtils.CreateBatchPublisher(context);

        var newPlaceholderJobId = await batchPublisher.ContinueBatchWith(requests, placeholderJobId);

        return newPlaceholderJobId;
    }

    protected async Task<Job> GetJobWithStates(TestContext context, Guid jobId)
    {
        var job = await context.Set<Job>()
            .Where(x => x.Id == jobId)
            .Include(x => x.JobStates.OrderBy(s => s.DateTime))
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
        await EnsureServerRegistered();

        var worker = TestUtils.CreateJoblyWorkerService(_serviceScopeFactory);

        await worker.GetAndProcessJob(CancellationToken.None);
    }

    protected async Task<bool> TryProcessJob()
    {
        await EnsureServerRegistered();

        var worker = TestUtils.CreateJoblyWorkerService(_serviceScopeFactory);
        return await worker.GetAndProcessJob(CancellationToken.None);
    }

    protected MultiHandlerCounter GetMultiHandlerCounter()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<MultiHandlerCounter>()!;
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

    protected async Task<Job> GetJob(Guid id)
    {
        var context = CreateContext();

        var job = await context.Set<Job>()
            .Where(x => x.Id == id)
            .SingleOrDefaultAsync();

        return job;
    }
}