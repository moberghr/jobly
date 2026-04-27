using System.Threading;
using System.Threading.Tasks;
using Mediator;

namespace Warp.Benchmarks.SourceGenMediator;

public sealed class SourceGenPingRequest : Mediator.IRequest<SourceGenPingResponse>
{
    public static readonly SourceGenPingRequest Instance = new();
}

public sealed class SourceGenPingResponse
{
    public static readonly SourceGenPingResponse Instance = new();
}

public sealed class SourceGenPingHandler : Mediator.IRequestHandler<SourceGenPingRequest, SourceGenPingResponse>
{
    public ValueTask<SourceGenPingResponse> Handle(SourceGenPingRequest request, CancellationToken cancellationToken)
    {
        return new ValueTask<SourceGenPingResponse>(SourceGenPingResponse.Instance);
    }
}

public sealed class SourceGenPassthroughBehavior1 : Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>
{
    public ValueTask<SourceGenPingResponse> Handle(SourceGenPingRequest message, CancellationToken cancellationToken, Mediator.MessageHandlerDelegate<SourceGenPingRequest, SourceGenPingResponse> next)
    {
        return next(message, cancellationToken);
    }
}

public sealed class SourceGenPassthroughBehavior2 : Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>
{
    public ValueTask<SourceGenPingResponse> Handle(SourceGenPingRequest message, CancellationToken cancellationToken, Mediator.MessageHandlerDelegate<SourceGenPingRequest, SourceGenPingResponse> next)
    {
        return next(message, cancellationToken);
    }
}

public sealed class SourceGenPassthroughBehavior3 : Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>
{
    public ValueTask<SourceGenPingResponse> Handle(SourceGenPingRequest message, CancellationToken cancellationToken, Mediator.MessageHandlerDelegate<SourceGenPingRequest, SourceGenPingResponse> next)
    {
        return next(message, cancellationToken);
    }
}

public sealed class SourceGenPassthroughBehavior4 : Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>
{
    public ValueTask<SourceGenPingResponse> Handle(SourceGenPingRequest message, CancellationToken cancellationToken, Mediator.MessageHandlerDelegate<SourceGenPingRequest, SourceGenPingResponse> next)
    {
        return next(message, cancellationToken);
    }
}

public sealed class SourceGenPassthroughBehavior5 : Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>
{
    public ValueTask<SourceGenPingResponse> Handle(SourceGenPingRequest message, CancellationToken cancellationToken, Mediator.MessageHandlerDelegate<SourceGenPingRequest, SourceGenPingResponse> next)
    {
        return next(message, cancellationToken);
    }
}
