using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core.Handlers;

namespace Warp.Core.Mutex;

public static class MutexServiceConfiguration
{
    public static IWarpBuilder<TContext> AddMutex<TContext>(this IWarpBuilder<TContext> builder)
        where TContext : DbContext
    {
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MutexPipelineBehavior<,>));
        builder.Services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(MutexPublishBehavior<>));

        return builder;
    }
}
