using Warp.Core;
using Warp.Core.Handlers;
using Warp.Core.Retry;
using Warp.Core.Sagas;
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
            options.AddSagas();
        });
        services.AddSagaHandler<OrderSagaWorkflow>();
    })
    .Build();

await host.RunAsync();
