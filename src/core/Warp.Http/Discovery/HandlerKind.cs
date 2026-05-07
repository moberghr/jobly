namespace Warp.Http.Discovery;

/// <summary>
/// The shape of the handler that a <see cref="HttpEndpointDescriptor"/> dispatches to.
/// HTTP exposure is supported for <see cref="Warp.Core.Handlers.IRequest{TResponse}"/>
/// and <see cref="Warp.Core.Handlers.IStreamRequest{TResponse}"/> only.
/// </summary>
public enum HandlerKind
{
    /// <summary>
    /// <see cref="Warp.Core.Handlers.IRequest{TResponse}"/> — dispatched via <c>IMediator.Send</c>.
    /// </summary>
    Request = 1,

    /// <summary>
    /// <see cref="Warp.Core.Handlers.IStreamRequest{TResponse}"/> — dispatched via <c>IMediator.CreateStream</c>.
    /// </summary>
    Stream = 2,
}
