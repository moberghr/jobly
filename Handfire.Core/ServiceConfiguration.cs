using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Handfire.Core;

public static class ServiceConfiguration
{
    public static void AddHandfireServices(this IServiceCollection services, IConfiguration configuration)
    {
        //services.AddMediatR(typeof(ServiceConfiguration));

        //services.AddDbContext<HandfireContext>(options => options
        //    .UseNpgsql(configuration.GetConnectionString(nameof(HandfireContext))!)
        //    .UseSnakeCaseNamingConvention());

        //services.AddScoped<IPublisher, Publisher>();

        //services.AddHostedService<HandfireWorker>();
    }
}
