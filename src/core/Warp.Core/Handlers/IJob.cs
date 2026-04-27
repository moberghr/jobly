namespace Warp.Core.Handlers;

/// <summary>
/// Marker interface for job messages. All messages published through Warp must implement this.
/// </summary>
public interface IJob : IRequest<Unit>;
