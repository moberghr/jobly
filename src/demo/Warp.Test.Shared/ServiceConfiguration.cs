using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core;

namespace Warp.Test.Shared;

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
