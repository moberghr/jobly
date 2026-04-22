using Jobly.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Test.Shared;

public static class ServiceConfiguration
{
    public static void AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextPool<TestContext>(options => options
            .UseNpgsql(configuration.GetConnectionString(nameof(TestContext))!)

            // .UseSqlServer(configuration.GetConnectionString(nameof(TestContext))!)
            .UseSnakeCaseNamingConvention());
    }
}
