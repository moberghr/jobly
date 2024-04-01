using Microsoft.EntityFrameworkCore;

namespace Jobly.Worker.Interceptors;

public interface IJobInterceptor
{
    Task<InterceptionResult> JobWillExecuteAsync(JobExecutingContext context, InterceptionResult result,
        CancellationToken cancellationToken);

    Task JobExecutionFailedAsync(JobExecutingContext context, CancellationToken cancellationToken);

    Task JobExecutedAsync(JobExecutingContext context, CancellationToken cancellationToken);
}

public abstract class JobInterceptor : IJobInterceptor
{
    public virtual Task<InterceptionResult> JobWillExecuteAsync(JobExecutingContext context,
        InterceptionResult result,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(result);
    }

    public virtual Task JobExecutionFailedAsync(JobExecutingContext context,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual Task JobExecutedAsync(JobExecutingContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}