using Warp.Core.Enums;
using Warp.Core.Handlers;

namespace Warp.Core.Mutex;

public class MutexPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IJobContext _jobContext;
    private readonly IWarpLockProvider _lockProvider;
    private readonly TimeProvider _timeProvider;

    public MutexPipelineBehavior(IJobContext jobContext, IWarpLockProvider lockProvider, TimeProvider timeProvider)
    {
        _jobContext = jobContext;
        _lockProvider = lockProvider;
        _timeProvider = timeProvider;
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
            var mode = meta.Mode ?? MutexMode.Skip;
            _jobContext.Outcome = mode == MutexMode.Wait
                ? BuildRequeueOutcome(meta.ConcurrencyKey)
                : BuildSkipOutcome(meta.ConcurrencyKey);

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

    private JobOutcome BuildRequeueOutcome(string key)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        return new JobOutcome
        {
            State = JobOutcome.RescheduledState(now, now),
            ScheduleTime = now,
            ClearHandlerType = true,
            LogMessage = $"Requeued — mutex '{key}' held by another job",
        };
    }

    private static JobOutcome BuildSkipOutcome(string key) =>
        new()
        {
            State = State.Deleted,
            LogMessage = $"Cancelled — mutex '{key}' held by another job",
        };
}
