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
        });
        
    })
    .Build();

await host.RunAsync();