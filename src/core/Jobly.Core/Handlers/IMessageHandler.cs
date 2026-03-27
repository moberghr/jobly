namespace Jobly.Core.Handlers;

/// <summary>
/// Handles a queue message. Multiple handlers can be registered for the same message type —
/// each will be executed as an independent job.
/// </summary>
public interface IMessageHandler<in T> where T : IMessage
{
    Task HandleAsync(T message, CancellationToken cancellationToken);
}
