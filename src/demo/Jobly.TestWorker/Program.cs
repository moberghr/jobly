using Jobly.Core;
using Jobly.Core.Retry;
using Jobly.Provider.PostgreSql;
using Jobly.Test.Shared;
using Jobly.Worker;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddServices(context.Configuration);
        services.AddJoblyWorker<TestContext>(options =>
        {
            options.UsePostgreSql();
            options.WorkerCount = 10;
            options.PollingInterval = TimeSpan.FromSeconds(5);
            options.AddRetry(o => o.MaxRetries = 3);
        });
    })
    .Build();

await host.RunAsync();
