using Jobly.Core.Entities;
using Jobly.Worker.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

public interface IInterceptorService
{
    JobExecutingContext CreateInterceptorPipeline(DbContext context, Job job, IServiceScope scope);

    Task RunWillExecuteInterceptors(JobExecutingContext executingContext, CancellationToken cancellationToken);

    Task RunJobExecutedInterceptors(JobExecutingContext executingContext, CancellationToken cancellationToken);

    Task RunJobExecutionFailedInterceptors(JobExecutingContext executingContext, CancellationToken cancellationToken);
}

internal class InterceptorService : IInterceptorService
{
    // Adding Built-in interceptors
    private readonly List<IJobInterceptor> _builtinInterceptors = new()
    {
        new RecurringInterceptor(),
        new RetryInterceptor(),
        new ContinuationInterceptor(),
        new BatchInterceptor(),
    };

    private readonly JoblyWorkerConfiguration _configuration;

    public InterceptorService(IOptions<JoblyWorkerConfiguration> configuration)
    {
        _configuration = configuration.Value;
    }

    public JobExecutingContext CreateInterceptorPipeline(DbContext context, Job job, IServiceScope scope)
    {
        var interceptors = _builtinInterceptors.ToList();

        // Adding user provided interceptors
        interceptors.AddRange(_configuration
            .Interceptors.Select(x => (IJobInterceptor) scope.ServiceProvider.GetRequiredService(x)).ToList());

        return new JobExecutingContext(context, job, interceptors);
    }

    public async Task RunWillExecuteInterceptors(JobExecutingContext executingContext,
        CancellationToken cancellationToken)
    {
        foreach (var interceptor in executingContext.Interceptors)
        {
            await interceptor.JobWillExecuteAsync(executingContext, cancellationToken);
            if (executingContext.IsSuppressed)
            {
                return;
            }
        }
    }

    public async Task RunJobExecutedInterceptors(JobExecutingContext executingContext,
        CancellationToken cancellationToken)
    {
        foreach (var interceptor in executingContext.Interceptors)
        {
            await interceptor.JobExecutedAsync(executingContext, cancellationToken);
        }
    }

    public async Task RunJobExecutionFailedInterceptors(JobExecutingContext executingContext,
        CancellationToken cancellationToken)
    {
        foreach (var interceptor in executingContext.Interceptors)
        {
            await interceptor.JobExecutionFailedAsync(executingContext, cancellationToken);
        }
    }
}