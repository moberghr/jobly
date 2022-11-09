using Handfire.Core;
using Handfire.Test.Shared;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddServices(context.Configuration);
        services.AddHandfire<TestContext>();
    })
    .Build();

await host.RunAsync();
