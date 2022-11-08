using Handfire.Core;
using Handfire.Core.Worker;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Handfire.Test.Shared;

public static class ServiceConfiguration
{
    public static void AddServices<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : HandfireContext
    {
        services.AddMediatR(typeof(ServiceConfiguration));

        services.AddDbContext<TContext>(options => options
            .UseNpgsql(configuration.GetConnectionString(nameof(TestContext))!)
            .UseSnakeCaseNamingConvention());

        services.AddScoped(typeof(Core.IPublisher<>), typeof(Publisher<>));

        services.AddHostedService<HandfireWorker<TContext>>();
    }
}
