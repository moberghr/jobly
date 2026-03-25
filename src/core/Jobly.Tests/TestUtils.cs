using Jobly.Core;
using Jobly.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobly.Tests;

public static class TestUtils
{
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
            WorkerCount = 1
        });
        
        return new JoblyWorkerService<TestContext>(serviceScopeFactory,
            new NullLogger<JoblyWorkerService<TestContext>>(),
            joblyWorkerConfigOptions);
    }
}