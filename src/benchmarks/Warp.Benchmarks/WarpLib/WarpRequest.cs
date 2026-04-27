using System.Threading;
using System.Threading.Tasks;
using Warp.Core.Handlers;

namespace Warp.Benchmarks.WarpLib;

public sealed class WarpPingRequest : IRequest<WarpPingResponse>
{
    public static readonly WarpPingRequest Instance = new();
}

public sealed class WarpPingResponse
{
    public static readonly WarpPingResponse Instance = new();
}

public sealed class WarpPingHandler : IRequestHandler<WarpPingRequest, WarpPingResponse>
{
    public Task<WarpPingResponse> HandleAsync(WarpPingRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(WarpPingResponse.Instance);
    }
}

public sealed class WarpPassthroughBehavior1 : IPipelineBehavior<WarpPingRequest, WarpPingResponse>
{
    public Task<WarpPingResponse> HandleAsync(WarpPingRequest request, RequestHandlerDelegate<WarpPingRequest, WarpPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class WarpPassthroughBehavior2 : IPipelineBehavior<WarpPingRequest, WarpPingResponse>
{
    public Task<WarpPingResponse> HandleAsync(WarpPingRequest request, RequestHandlerDelegate<WarpPingRequest, WarpPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class WarpPassthroughBehavior3 : IPipelineBehavior<WarpPingRequest, WarpPingResponse>
{
    public Task<WarpPingResponse> HandleAsync(WarpPingRequest request, RequestHandlerDelegate<WarpPingRequest, WarpPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class WarpPassthroughBehavior4 : IPipelineBehavior<WarpPingRequest, WarpPingResponse>
{
    public Task<WarpPingResponse> HandleAsync(WarpPingRequest request, RequestHandlerDelegate<WarpPingRequest, WarpPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class WarpPassthroughBehavior5 : IPipelineBehavior<WarpPingRequest, WarpPingResponse>
{
    public Task<WarpPingResponse> HandleAsync(WarpPingRequest request, RequestHandlerDelegate<WarpPingRequest, WarpPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}
