using System.Collections.Concurrent;
using System.Reflection;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Microsoft.Extensions.Options;

namespace Jobly.Core.Retry;

public class RetryPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, RetryAttribute?> AttributeCache = new();

    private readonly IJobContext _jobContext;
    private readonly IOptions<RetryOptions> _options;
    private readonly TimeProvider _timeProvider;

    public RetryPipelineBehavior(IJobContext jobContext, IOptions<RetryOptions> options, TimeProvider timeProvider)
    {
        _jobContext = jobContext;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(request, cancellationToken);
        }
        catch (Exception) when (request is IJob)
        {
            var meta = _jobContext.GetMetadata<IRetryMetadata>();
            var attr = GetRetryAttribute();
            var maxRetries = meta.MaxRetries ?? attr?.MaxRetries ?? _options.Value.MaxRetries;
            var retriedTimes = meta.RetriedTimes;

            if (retriedTimes < maxRetries)
            {
                var delays = meta.RetryDelays ?? attr?.Delays ?? _options.Value.Delays;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                DateTime? scheduleTime = null;

                if (delays.Length > 0)
                {
                    var idx = Math.Min(retriedTimes, delays.Length - 1);
                    scheduleTime = now + TimeSpan.FromSeconds(delays[idx]);
                }

                meta.RetriedTimes = retriedTimes + 1;

                _jobContext.Outcome = new JobOutcome
                {
                    State = State.Enqueued,
                    ScheduleTime = scheduleTime,
                    ClearHandlerType = true,
                };
            }

            throw;
        }
    }

    private RetryAttribute? GetRetryAttribute()
    {
        var handlerType = _jobContext.HandlerType;
        if (handlerType != null)
        {
            var handlerAttr = AttributeCache.GetOrAdd(handlerType, static t => t.GetCustomAttribute<RetryAttribute>());
            if (handlerAttr != null)
            {
                return handlerAttr;
            }
        }

        return AttributeCache.GetOrAdd(typeof(TRequest), static t => t.GetCustomAttribute<RetryAttribute>());
    }
}
