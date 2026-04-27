using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core.Handlers;

namespace Warp.Core.NoRestart;

public static class NoRestartServiceConfiguration
{
    public static IWarpBuilder<TContext> AddNoRestart<TContext>(this IWarpBuilder<TContext> builder)
        where TContext : DbContext
    {
        builder.Services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(NoRestartPublishBehavior<>));

        return builder;
    }
}
