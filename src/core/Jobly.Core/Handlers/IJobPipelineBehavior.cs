namespace Jobly.Core.Handlers;

public delegate Task JobHandlerDelegate();

/// <summary>
/// Pipeline behavior that wraps handler execution for both IJob and IMessage types.
/// Call <paramref name="next"/> to continue the pipeline.
/// </summary>
public interface IPipelineBehavior<in T>
    where T : class
{
    Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken cancellationToken);
}
