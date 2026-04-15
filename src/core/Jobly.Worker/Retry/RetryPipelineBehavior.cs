using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Retry;

public class RetryPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IJobContext<IRetryMetadata> _jobContext;
    private readonly IOptions<RetryOptions> _options;
    private readonly TimeProvider _timeProvider;

    public RetryPipelineBehavior(IJobContext<IRetryMetadata> jobContext, IOptions<RetryOptions> options, TimeProvider timeProvider)
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
            var meta = _jobContext.Metadata;
            var maxRetries = meta.MaxRetries ?? _options.Value.MaxRetries;
            var retriedTimes = meta.RetriedTimes;

            if (retriedTimes < maxRetries)
            {
                var delays = meta.RetryDelays ?? _options.Value.Delays;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                DateTime? scheduleTime = null;

                if (delays.Length > 0)
                {
                    var idx = Math.Min(retriedTimes, delays.Length - 1);
                    scheduleTime = now + TimeSpan.FromSeconds(delays[idx]);
                }

                meta.RetriedTimes = retriedTimes + 1;

                _jobContext.FailureOutcome = new JobFailureOutcome
                {
                    State = State.Enqueued,
                    ScheduleTime = scheduleTime,
                    ClearHandlerType = true,
                };
            }

            throw;
        }
    }
}
