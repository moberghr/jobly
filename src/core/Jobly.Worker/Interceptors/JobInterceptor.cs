using Microsoft.EntityFrameworkCore;

namespace Jobly.Worker.Interceptors;

/// <summary>
/// Intercepts job execution. Implementations can be used to modify the job execution flow.
///
/// This interface is still very much in progress and will be updated as we go.
/// This pipeline is inspired by the Ef Core Interceptors, but we might go with a single execution pipeline like in Asp.Net Core.
/// middleware and grpc interceptors.
/// </summary>
public interface IJobInterceptor
{
    
    /// <summary>
    /// Will be called before the job is executed. Here you can suppress the job execution by returning <see cref="InterceptionResult.Suppress" />.
    /// You can also do some pre-execution work here.
    /// 
    /// </summary>
    /// <param name="context">JobExecutingContext contains the information about the processing flow</param>
    /// <param name="result"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<InterceptionResult> JobWillExecuteAsync(JobExecutingContext context, InterceptionResult result,
        CancellationToken cancellationToken);

    /// <summary>
    /// Called if the job execution failed.
    /// </summary>
    /// <param name="context">JobExecutingContext contains the information about the processing flow</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task JobExecutionFailedAsync(JobExecutingContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called after the job has been executed.
    /// </summary>
    /// <param name="context">JobExecutingContext contains the information about the processing flow</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task JobExecutedAsync(JobExecutingContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Convenience base class for <see cref="IJobInterceptor" /> implementations.
/// </summary>
public abstract class JobInterceptor : IJobInterceptor
{
    /// <inheritdoc />
    public virtual Task<InterceptionResult> JobWillExecuteAsync(JobExecutingContext context,
        InterceptionResult result,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public virtual Task JobExecutionFailedAsync(JobExecutingContext context,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task JobExecutedAsync(JobExecutingContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}