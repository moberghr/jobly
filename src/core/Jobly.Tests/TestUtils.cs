using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Services;
using Jobly.Worker;
using Jobly.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobly.Tests;

public static class TestUtils
{
    private static readonly string[] DefaultQueues = ["a-critical", "b-default", "c-low", "default", "high"];
    public static readonly Guid TestServerId = Guid.NewGuid();
    public static readonly Guid TestWorkerId = Guid.NewGuid();

    public static async Task RegisterTestServer(TestContext context, int workerCount = 1)
    {
        var now = DateTime.UtcNow;
        var server = new Server
        {
            Id = TestServerId,
            StartedTime = now,
            LastHeartbeatTime = now,
            ServiceCount = workerCount,
        };
        await context.Set<Server>().AddAsync(server);

        var worker = new Jobly.Core.Data.Entities.Worker
        {
            Id = TestWorkerId,
            ServerId = TestServerId,
            StartedTime = now,
            LastHeartbeatTime = now,
        };
        await context.Set<Jobly.Core.Data.Entities.Worker>().AddAsync(worker);

        await context.SaveChangesAsync();
    }

    public static JobQueryService<TestContext> CreateJobQueryService(TestContext context)
    {
        return new JobQueryService<TestContext>(context);
    }

    public static JobCommandService<TestContext> CreateJobCommandService(TestContext context)
    {
        return new JobCommandService<TestContext>(context);
    }

    public static JobGroupQueryService<TestContext> CreateJobGroupQueryService(TestContext context)
    {
        return new JobGroupQueryService<TestContext>(context);
    }

    public static RecurringJobService<TestContext> CreateRecurringJobService(TestContext context)
    {
        return new RecurringJobService<TestContext>(context);
    }

    public static DashboardStatsService<TestContext> CreateDashboardStatsService(TestContext context)
    {
        return new DashboardStatsService<TestContext>(context);
    }

    public static Publisher<TestContext> CreatePublisher(TestContext context, int retries = 0)
    {
        IOptions<JoblyConfiguration> joblyConfigOptions = new OptionsWrapper<JoblyConfiguration>(new JoblyConfiguration
        {
            RetryCount = retries,
        });
        return new Publisher<TestContext>(context, joblyConfigOptions);
    }

    public static BatchPublisher<TestContext> CreateBatchPublisher(TestContext context, int retries = 0)
    {
        IOptions<JoblyConfiguration> joblyConfigOptions = new OptionsWrapper<JoblyConfiguration>(new JoblyConfiguration
        {
            RetryCount = retries,
        });
        return new BatchPublisher<TestContext>(context, joblyConfigOptions);
    }

    public static async Task AggregateCounters(TestContext context)
    {
        await CounterAggregatorTask<TestContext>.AggregateCounters(context);
    }

    public static IOptions<JoblyWorkerConfiguration> CreateWorkerConfig()
    {
        return new OptionsWrapper<JoblyWorkerConfiguration>(new JoblyWorkerConfiguration
        {
            WorkerCount = 1,
            ServerId = TestServerId,
            Queues = DefaultQueues,
        });
    }

    public static WorkerGroupConfiguration CreateGroupConfig()
    {
        return new WorkerGroupConfiguration
        {
            WorkerCount = 1,
            Queues = DefaultQueues,
        };
    }

    public static JoblyWorkerService<TestContext> CreateJoblyWorkerService(IServiceScopeFactory serviceScopeFactory)
    {
        return new JoblyWorkerService<TestContext>(
            TestWorkerId,
            serviceScopeFactory,
            new NullLogger<JoblyWorkerService<TestContext>>(),
            CreateWorkerConfig(),
            CreateGroupConfig());
    }

    public static JoblyDispatcher<TestContext> CreateDispatcher(IServiceScopeFactory serviceScopeFactory)
    {
        return new JoblyDispatcher<TestContext>(
            serviceScopeFactory,
            new NullLogger<JoblyDispatcher<TestContext>>(),
            CreateWorkerConfig(),
            CreateGroupConfig());
    }

    public static JoblyDispatcherWorker<TestContext> CreateDispatcherWorker(
        IServiceScopeFactory serviceScopeFactory,
        System.Threading.Channels.ChannelReader<Jobly.Core.Entities.Job> jobReader)
    {
        return new JoblyDispatcherWorker<TestContext>(
            TestWorkerId,
            jobReader,
            serviceScopeFactory,
            new NullLogger<JoblyDispatcherWorker<TestContext>>(),
            CreateWorkerConfig());
    }
}
