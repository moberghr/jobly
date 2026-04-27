using System.Threading;
using System.Threading.Tasks;
using MediatR;

namespace Warp.Benchmarks.MediatRLib;

public sealed class MediatRPingRequest : MediatR.IRequest<MediatRPingResponse>
{
    public static readonly MediatRPingRequest Instance = new();
}

public sealed class MediatRPingResponse
{
    public static readonly MediatRPingResponse Instance = new();
}

public sealed class MediatRPingHandler : MediatR.IRequestHandler<MediatRPingRequest, MediatRPingResponse>
{
    public Task<MediatRPingResponse> Handle(MediatRPingRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(MediatRPingResponse.Instance);
    }
}

public sealed class MediatRPassthroughBehavior1 : MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>
{
    public Task<MediatRPingResponse> Handle(MediatRPingRequest request, MediatR.RequestHandlerDelegate<MediatRPingResponse> next, CancellationToken cancellationToken)
    {
        return next(cancellationToken);
    }
}

public sealed class MediatRPassthroughBehavior2 : MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>
{
    public Task<MediatRPingResponse> Handle(MediatRPingRequest request, MediatR.RequestHandlerDelegate<MediatRPingResponse> next, CancellationToken cancellationToken)
    {
        return next(cancellationToken);
    }
}

public sealed class MediatRPassthroughBehavior3 : MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>
{
    public Task<MediatRPingResponse> Handle(MediatRPingRequest request, MediatR.RequestHandlerDelegate<MediatRPingResponse> next, CancellationToken cancellationToken)
    {
        return next(cancellationToken);
    }
}

public sealed class MediatRPassthroughBehavior4 : MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>
{
    public Task<MediatRPingResponse> Handle(MediatRPingRequest request, MediatR.RequestHandlerDelegate<MediatRPingResponse> next, CancellationToken cancellationToken)
    {
        return next(cancellationToken);
    }
}

public sealed class MediatRPassthroughBehavior5 : MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>
{
    public Task<MediatRPingResponse> Handle(MediatRPingRequest request, MediatR.RequestHandlerDelegate<MediatRPingResponse> next, CancellationToken cancellationToken)
    {
        return next(cancellationToken);
    }
}
