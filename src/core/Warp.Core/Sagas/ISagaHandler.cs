using Warp.Core.Handlers;

namespace Warp.Core.Sagas;

/// <summary>
/// Handles a single message type against a saga instance. One handler class can implement
/// <see cref="ISagaHandler{TSaga, TMessage}"/> for multiple message types — each implementation
/// is registered as an <c>IMessageHandler&lt;TMessage&gt;</c> via a generated proxy.
/// </summary>
/// <typeparam name="TSaga">The saga type. State lives here.</typeparam>
/// <typeparam name="TMessage">The message type. Must carry a <see cref="CorrelateAttribute"/> property.</typeparam>
public interface ISagaHandler<in TSaga, in TMessage>
    where TSaga : Saga, new()
    where TMessage : class, IMessage
{
    /// <summary>
    /// Invoked when a message arrives for a live saga. Mutate <paramref name="saga"/> in place;
    /// the pipeline proxy persists the changes after this returns. Call
    /// <c>saga.MarkCompleted()</c> to delete the saga on save.
    /// </summary>
    Task HandleAsync(TSaga saga, TMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when a non-<see cref="StartsSagaAttribute"/> message arrives for an unknown
    /// correlation key. The pipeline sets a default <c>State = Failed</c> outcome on
    /// <paramref name="context"/> <em>before</em> calling this method; override and set
    /// <c>context.Outcome</c> to <c>{ State = Deleted, ... }</c> to silently ignore the message,
    /// or leave the default to surface the missing-saga case as a job failure.
    /// </summary>
    /// <remarks>
    /// The default implementation is a no-op, which means the proxy's default outcome stands.
    /// Lifted from Wolverine's <c>NotFound</c> hook.
    /// </remarks>
    Task NotFoundAsync(TMessage message, IJobContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
