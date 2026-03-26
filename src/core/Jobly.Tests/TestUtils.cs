using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobly.Tests;

public static class TestUtils
{
    public static readonly Guid TestServerId = Guid.NewGuid();

    public static async Task RegisterTestServer(TestContext context, int workerCount = 1)
    {
        var now = DateTime.UtcNow;
        var server = new Server
        {
            Id = TestServerId,
            StartedTime = now,
            LastHeartbeatTime = now,
            ServiceCount = workerCount
        };
        await context.Set<Server>().AddAsync(server);
        await context.SaveChangesAsync();
    }

    public static JoblyService<TestContext> CreateJoblyService(TestContext context)
    {
        return new JoblyService<TestContext>(context);
    }

    public static Publisher<TestContext> CreatePublisher(TestContext context, int retries = 0)
    {
        IOptions<JoblyConfiguration> joblyConfigOptions = new OptionsWrapper<JoblyConfiguration>(new JoblyConfiguration
        {
            RetryCount = retries
        });
        return new Publisher<TestContext>(context, joblyConfigOptions);
    }

    public static BatchPublisher<TestContext> CreateBatchPublisher(TestContext context, int retries = 0)
    {
        IOptions<JoblyConfiguration> joblyConfigOptions = new OptionsWrapper<JoblyConfiguration>(new JoblyConfiguration
        {
            RetryCount = retries
        });
        return new BatchPublisher<TestContext>(context, joblyConfigOptions);
    }

    public static JoblyWorkerService<TestContext> CreateJoblyWorkerService(IServiceScopeFactory serviceScopeFactory)
    {
        IOptions<JoblyWorkerConfiguration> joblyWorkerConfigOptions = new OptionsWrapper<JoblyWorkerConfiguration>(new JoblyWorkerConfiguration
        {
            WorkerCount = 1,
            ServerId = TestServerId
        });

        return new JoblyWorkerService<TestContext>(serviceScopeFactory,
            new NullLogger<JoblyWorkerService<TestContext>>(),
            joblyWorkerConfigOptions);
    }
}