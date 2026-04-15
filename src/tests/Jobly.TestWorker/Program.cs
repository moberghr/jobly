using Jobly.Core;
using Jobly.Core.Retry;
using Jobly.Test.Shared;
using Jobly.Worker;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddServices(context.Configuration);
        services.AddJoblyRetry(o => o.MaxRetries = 3);
        services.AddJoblyWorker<TestContext>(options =>
        {
            options.WorkerCount = 10;
            options.PollingInterval = TimeSpan.FromSeconds(5);
        });
    })
    .Build();

await host.RunAsync();
