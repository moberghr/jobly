using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Services;
using Jobly.Worker;
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

    public static MessageQueryService<TestContext> CreateMessageQueryService(TestContext context)
    {
        return new MessageQueryService<TestContext>(context);
    }

    public static RecurringJobService<TestContext> CreateRecurringJobService(TestContext context)
    {
        return new RecurringJobService<TestContext>(context);
    }

    public static BatchQueryService<TestContext> CreateBatchQueryService(TestContext context)
    {
        return new BatchQueryService<TestContext>(context);
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
        await JoblyHealthManager<TestContext>.AggregateCounters(context);
    }

    public static JoblyWorkerService<TestContext> CreateJoblyWorkerService(IServiceScopeFactory serviceScopeFactory)
    {
        IOptions<JoblyWorkerConfiguration> joblyWorkerConfigOptions = new OptionsWrapper<JoblyWorkerConfiguration>(new JoblyWorkerConfiguration
        {
            WorkerCount = 1,
            ServerId = TestServerId,
            Queues = DefaultQueues,
        });

        return new JoblyWorkerService<TestContext>(
            TestWorkerId,
            serviceScopeFactory,
            new NullLogger<JoblyWorkerService<TestContext>>(),
            joblyWorkerConfigOptions);
    }
}
