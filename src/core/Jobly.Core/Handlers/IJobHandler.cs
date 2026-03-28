namespace Jobly.Core.Handlers;

/// <summary>
/// Handles a job message. Multiple handlers can be registered for the same message type —
/// each will be executed as an independent job.
/// </summary>
public interface IJobHandler<in T>
    where T : IJob
{
    Task HandleAsync(T message, CancellationToken cancellationToken);
}
