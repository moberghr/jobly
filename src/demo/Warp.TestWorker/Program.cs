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

            // Push finalize/enqueue notifications so the dashboard (running in TestApp)
            // sees this worker's job lifecycle events too. Without this, push would be
            // limited to the TestApp's own worker pool — TestWorker activity would only
            // appear on the dashboard via the 30s safety-net poll.
            options.UseDatabasePush();
        });
        services.AddSagaHandler<OrderSagaWorkflow>();
    })
    .Build();

await host.RunAsync();
