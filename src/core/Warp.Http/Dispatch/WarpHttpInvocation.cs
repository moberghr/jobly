using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core.Handlers;

namespace Warp.Http.Dispatch;

/// <summary>
/// <b>Generated-code support — not intended for direct use.</b> The dispatch trampoline
/// that the source generator emits a closed call to. Resolves <see cref="IMediator"/>
/// from the request scope, runs the existing <c>IPipelineBehavior</c> chain, and writes
/// the response.
/// </summary>
public static class WarpHttpInvocation
{
    public static async Task InvokeRequest<TRequest, TResponse>(HttpContext context, TRequest request)
        where TRequest : IRequest<TResponse>
    {
        var mediator = context.RequestServices.GetRequiredService<IMediator>();
        var response = await mediator.Send<TResponse>(request, context.RequestAborted).ConfigureAwait(false);
        await JsonResponseWriter.WriteAsync(context, response, context.RequestAborted).ConfigureAwait(false);
    }

    public static async Task InvokeStream<TRequest, TResponse>(HttpContext context, TRequest request)
        where TRequest : IStreamRequest<TResponse>
    {
        var mediator = context.RequestServices.GetRequiredService<IMediator>();
        var stream = mediator.CreateStream<TResponse>(request, context.RequestAborted);
        await SseResponseWriter.WriteAsync(context, stream, context.RequestAborted).ConfigureAwait(false);
    }
}
