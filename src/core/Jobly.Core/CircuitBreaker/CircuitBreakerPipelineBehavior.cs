using System.Collections.Concurrent;
using System.Reflection;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Microsoft.Extensions.Options;

namespace Jobly.Core.CircuitBreaker;

public class CircuitBreakerPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IJob
{
    private static readonly ConcurrentDictionary<Type, CircuitBreakerAttribute?> AttributeCache = new();

    private readonly IJobContext _jobContext;
    private readonly IOptions<CircuitBreakerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ICircuitBreakerStore _store;

    public CircuitBreakerPipelineBehavior(
        IJobContext jobContext,
        IOptions<CircuitBreakerOptions> options,
        TimeProvider timeProvider,
        ICircuitBreakerStore store)
    {
        _jobContext = jobContext;
        _options = options;
        _timeProvider = timeProvider;
        _store = store;
    }

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken)
    {
        var attr = GetCircuitBreakerAttribute();
        var groupKey = attr?.Group ?? typeof(TRequest).Name;
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var state = await _store.GetAsync(groupKey, cancellationToken);
        if (state?.OpenUntil is { } openUntil && openUntil > now)
        {
            var jitter = attr?.GetResetJitter(options) ?? options.ResetJitter;
            var jitterMs = (int)jitter.TotalMilliseconds;
            var delayMs = jitterMs > 0 ? Random.Shared.Next(0, jitterMs + 1) : 0;
            _jobContext.Outcome = new JobOutcome
            {
                State = State.Enqueued,
                ScheduleTime = openUntil.AddMilliseconds(delayMs),
                LogMessage = $"Rescheduled due to open circuit breaker '{groupKey}'",
            };

            return default!;
        }

        try
        {
            var response = await next(request, cancellationToken);

            // Only reset on a real handler success. If another behavior (e.g. Mutex) set an Outcome,
            // the handler didn't actually complete — treat as a non-event for the circuit counter.
            // state is a pre-execution snapshot: if a concurrent worker incremented FailureCount during
            // next(), our snapshot may show 0 and we skip the reset. The next clean success corrects it.
            if (_jobContext.Outcome is null && state?.FailureCount > 0)
            {
                await _store.ResetAsync(groupKey, cancellationToken);
            }

            return response;
        }
        catch
        {
            if (_jobContext.Outcome is not null)
            {
                throw;
            }

            var threshold = attr?.GetThreshold(options) ?? options.Threshold;
            var duration = attr?.GetDuration(options) ?? options.Duration;
            await _store.RecordFailureAsync(groupKey, threshold, duration, now, cancellationToken);

            throw;
        }
    }

    private CircuitBreakerAttribute? GetCircuitBreakerAttribute()
    {
        var handlerType = _jobContext.HandlerType;
        if (handlerType != null)
        {
            var handlerAttr = AttributeCache.GetOrAdd(handlerType, static t => t.GetCustomAttribute<CircuitBreakerAttribute>());
            if (handlerAttr != null)
            {
                return handlerAttr;
            }
        }

        return AttributeCache.GetOrAdd(typeof(TRequest), static t => t.GetCustomAttribute<CircuitBreakerAttribute>());
    }
}
