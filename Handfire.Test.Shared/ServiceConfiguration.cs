using Handfire.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Handfire.Test.Shared;

public static class ServiceConfiguration
{
    public static void AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(typeof(ServiceConfiguration));

        services.AddDbContextPool<TestContext>(options => options
            .UseNpgsql(configuration.GetConnectionString(nameof(TestContext))!)
            //.UseSqlServer(configuration.GetConnectionString(nameof(TestContext))!)
            .UseSnakeCaseNamingConvention()
            .AddHandfireInterceptors());
    }
}
