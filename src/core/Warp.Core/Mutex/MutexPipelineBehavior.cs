using Warp.Core.Enums;
using Warp.Core.Handlers;

namespace Warp.Core.Mutex;

public class MutexPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IJobContext _jobContext;
    private readonly IWarpLockProvider _lockProvider;

    public MutexPipelineBehavior(IJobContext jobContext, IWarpLockProvider lockProvider)
    {
        _jobContext = jobContext;
        _lockProvider = lockProvider;
    }

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IJob)
        {
            return await next(request, cancellationToken);
        }

        var meta = _jobContext.GetMetadata<IMutexMetadata>();
        if (meta.ConcurrencyKey == null)
        {
            return await next(request, cancellationToken);
        }

        var handle = await _lockProvider.TryAcquireAsync(
            $"warp:mutex:{meta.ConcurrencyKey}",
            TimeSpan.Zero,
            cancellationToken);

        if (handle == null)
        {
            _jobContext.Outcome = new JobOutcome
            {
                State = State.Deleted,
                LogMessage = $"Cancelled — mutex '{meta.ConcurrencyKey}' held by another job",
            };

            return default!;
        }

        try
        {
            return await next(request, cancellationToken);
        }
        finally
        {
            await handle.DisposeAsync();
        }
    }
}
