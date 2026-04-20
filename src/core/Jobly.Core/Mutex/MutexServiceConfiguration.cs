using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.Mutex;

public static class MutexServiceConfiguration
{
    public static IJoblyBuilder<TContext> AddMutex<TContext>(this IJoblyBuilder<TContext> builder)
        where TContext : DbContext
    {
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MutexPipelineBehavior<,>));
        builder.Services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(MutexPublishBehavior<>));

        return builder;
    }
}
