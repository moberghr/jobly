using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.Retry;

public static class RetryServiceConfiguration
{
    public static IJoblyBuilder<TContext> AddRetry<TContext>(
        this IJoblyBuilder<TContext> builder,
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
