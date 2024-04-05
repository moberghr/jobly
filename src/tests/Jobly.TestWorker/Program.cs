using Jobly.Core;
using Jobly.Test.Shared;
using Jobly.Worker;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddServices(context.Configuration);
        services.AddJoblyWorker<TestContext>(options =>
        {
            options.WorkerCount = 10;
            options.RetryCount = 3;
            options.PollingInterval = TimeSpan.FromSeconds(5);
        });
    })
    .Build();

await host.RunAsync();