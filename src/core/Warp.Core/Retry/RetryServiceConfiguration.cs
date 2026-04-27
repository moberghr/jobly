using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core.Handlers;

namespace Warp.Core.Retry;

public static class RetryServiceConfiguration
{
    public static IWarpBuilder<TContext> AddRetry<TContext>(
        this IWarpBuilder<TContext> builder,
        Action<RetryOptions>? configure = null)
        where TContext : DbContext
    {
        if (configure != null)
        {
            builder.Services.Configure(configure);
        }
        else
        {
            builder.Services.AddOptions<RetryOptions>();
        }

        builder.Services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(RetryPublishBehavior<>));
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryPipelineBehavior<,>));

        return builder;
    }
}
