namespace Warp.Core.Handlers;

/// <summary>
/// Marker interface for queue messages. Messages can have multiple handlers.
/// Each handler becomes an independent job.
/// </summary>
public interface IMessage : IRequest<Unit>;
