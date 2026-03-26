namespace Jobly.Core.Handlers;

public delegate Task JobHandlerDelegate();

/// <summary>
/// Pipeline behavior that wraps job handler execution.
/// Call <paramref name="next"/> to continue the pipeline.
/// </summary>
public interface IJobPipelineBehavior<in T> where T : IJob
{
    Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken cancellationToken);
}
