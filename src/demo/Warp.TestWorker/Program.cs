using Warp.Core;
using Warp.Core.Retry;
using Warp.Provider.PostgreSql;
using Warp.Test.Shared;
using Warp.Worker;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddServices(context.Configuration);
        services.AddWarpWorker<TestContext>(options =>
        {
            options.UsePostgreSql();
            options.WorkerCount = 10;
            options.PollingInterval = TimeSpan.FromSeconds(5);
            options.AddRetry(o => o.MaxRetries = 3);
        });
    })
    .Build();

await host.RunAsync();
