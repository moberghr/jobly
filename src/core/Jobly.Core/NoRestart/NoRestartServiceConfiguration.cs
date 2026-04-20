using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.NoRestart;

public static class NoRestartServiceConfiguration
{
    public static IJoblyBuilder<TContext> AddNoRestart<TContext>(this IJoblyBuilder<TContext> builder)
        where TContext : DbContext
    {
        builder.Services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(NoRestartPublishBehavior<>));

        return builder;
    }
}
