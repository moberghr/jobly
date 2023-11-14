using Jobly.Core;
using Jobly.Test.Shared;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddServices(context.Configuration);
        services.AddJobly<TestContext>(2);
    })
    .Build();

await host.RunAsync();
