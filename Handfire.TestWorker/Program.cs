using Handfire.Core;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHandfireServices(context.Configuration);
    })
    .Build();

await host.RunAsync();
