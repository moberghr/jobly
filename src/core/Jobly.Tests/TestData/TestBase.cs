using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Handlers;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Tests;

public abstract class TestBase
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly Func<TestContext> _createContext;
    private readonly SemaphoreSlim _serverRegistrationLock = new(1, 1);
    private bool _serverRegistered;

    protected TestBase(Func<TestContext> createContext)
    {
        _createContext = createContext;

        var services = new ServiceCollection();
        var provider = services.AddJobHandlers(typeof(TestBase).Assembly)
            .AddPipelineBehaviors(typeof(TestBase).Assembly)
            .AddScoped<TestContext>(x => CreateContext())
            .AddJoblyWorker<TestContext>()
            .AddSingleton<CounterService>()
            .AddSingleton<MultiHandlerCounter>()
            .BuildServiceProvider();

        _serviceScopeFactory = provider.GetService<IServiceScopeFactory>()!;
    }

    protected TestContext CreateContext() => _createContext();

    public void ResetServerRegistration()
    {
        _serverRegistered = false;
    }

    protected async Task EnsureServerRegistered()
    {
        if (_serverRegistered)
        {
            return;
        }

        await _serverRegistrationLock.WaitAsync();
        try
        {
            if (_serverRegistered)
            {
                return;
            }

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
        var jobId = await publisher.Enqueue(processLogJob);
        await context.SaveChangesAsync();
        return jobId;
    }

    protected static async Task<Guid> CreateFailedJob(TestContext context)
    {
        var publisher = TestUtils.CreatePublisher(context);
        var throwExceptionRequest = new ThrowExceptionRequest();
        var jobId = await publisher.Enqueue(throwExceptionRequest);
        await context.SaveChangesAsync();
        return jobId;
    }

    protected static async Task<Guid> CreateFailedRetryJob(TestContext context, int retries, int? maxRetries, Guid? parentJobId)
    {
        var publisher = TestUtils.CreatePublisher(context, retries);
        var throwExceptionRequest = new ThrowExceptionRequest();
        Guid? jobId = null;
        if (maxRetries != null)
        {
            jobId = await publisher.Enqueue(throwExceptionRequest, (int)maxRetries);
        }

        if (parentJobId != null)
        {
            jobId = await publisher.Enqueue(throwExceptionRequest, parentJobId.Value);
        }

        if (maxRetries == null && parentJobId == null)
        {
            jobId = await publisher.Enqueue(throwExceptionRequest);
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
        await publisher.Enqueue(request);
        await context.SaveChangesAsync();
    }

    protected static async Task<int> CreateLogInDb(TestContext context)
    {
        var logInDb = new TestLog();
        await context.TestLogs.AddAsync(logInDb);
        await context.SaveChangesAsync();
        return logInDb.Id;
    }

    protected static async Task<Guid> CreateJobWithParentId(TestContext context, Guid parentJobId)
    {
        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new UnitRequest(), parentJobId);
        return jobId;
    }

    protected static async Task<Guid> CreateBatch(TestContext context, int numberOfJobs)
    {
        var requests = new List<UnitRequest>();
        for (var i = 0; i < numberOfJobs; i++)
        {
            requests.Add(new UnitRequest());
        }

        var batchPublisher = TestUtils.CreateBatchPublisher(context);
        return await batchPublisher.StartNew(requests);
    }

    protected static async Task<Guid> CreateBatchWithOptions(TestContext context, int numberOfJobs, BatchContinuationOptions options)
    {
        var requests = new List<UnitRequest>();
        for (var i = 0; i < numberOfJobs; i++)
        {
            requests.Add(new UnitRequest());
        }

        var batchPublisher = TestUtils.CreateBatchPublisher(context);
        return await batchPublisher.StartNew(requests, options);
    }

    protected static async Task<Guid> ContinueBatchWith(TestContext context, int numberOfJobs, Guid placeholderJobId)
    {
        var requests = new List<UnitRequest>();
        for (var i = 0; i < numberOfJobs; i++)
        {
            requests.Add(new UnitRequest());
        }

        var batchPublisher = TestUtils.CreateBatchPublisher(context);
        return await batchPublisher.ContinueBatchWith(requests, placeholderJobId);
    }

    protected static async Task<TestLog> GetTestLog(TestContext context, int testLogId)
    {
        return await context.TestLogs
            .Where(x => x.Id == testLogId)
            .AsNoTracking()
            .SingleAsync();
    }

    protected async Task<Message> GetMessage(Guid messageId)
    {
        return await CreateContext().Set<Message>()
            .Where(x => x.Id == messageId)
            .AsNoTracking()
            .SingleAsync();
    }

    protected async Task<List<Job>> GetJobsForMessage(Guid messageId)
    {
        return await CreateContext().Set<Job>()
            .Where(x => x.MessageId == messageId)
            .AsNoTracking()
            .ToListAsync();
    }

    protected async Task ProcessJob()
    {
        await EnsureServerRegistered();
        var worker = TestUtils.CreateJoblyWorkerService(_serviceScopeFactory);
        await worker.GetAndProcessJob(CancellationToken.None);
        await JoblyHealthManager<TestContext>.AggregateCounters(CreateContext());
    }

    protected async Task ProcessAllJobs(int workerCount = 1)
    {
        await EnsureServerRegistered();
        var tasks = new List<Task>();
        for (var i = 0; i < workerCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var worker = TestUtils.CreateJoblyWorkerService(_serviceScopeFactory);
                while (await worker.GetAndProcessJob(CancellationToken.None))
                {
                }
            }));
        }

        await Task.WhenAll(tasks);
        await JoblyHealthManager<TestContext>.AggregateCounters(CreateContext());
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
        return counterService!.Counter;
    }

    protected async Task<string> CreateUnitRecurringJob(string cronExpression)
    {
        var name = Guid.NewGuid().ToString();
        var context = CreateContext();
        var publisher = new RecurringJobPublisher<TestContext>(context);
        await publisher.AddOrUpdateRecurringJob(new UnitRequest(), name, cronExpression);
        return name;
    }

    protected async Task<RecurringJob?> GetRecurringJob(string name)
    {
        return await CreateContext().Set<RecurringJob>()
            .Where(x => x.Name == name)
            .SingleOrDefaultAsync();
    }

    protected async Task<Job?> GetJob(Guid id)
    {
        return await CreateContext().Set<Job>()
            .Where(x => x.Id == id)
            .SingleOrDefaultAsync();
    }
}
