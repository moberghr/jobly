using System.Text.Json;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Retry;

public class RetryPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
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
            var metadata = _jobContext.Metadata;
            var maxRetries = TryGetInt(metadata, "$maxRetries") ?? _options.Value.MaxRetries;
            var retriedTimes = TryGetInt(metadata, "$retriedTimes") ?? 0;

            if (retriedTimes < maxRetries)
            {
                var newRetriedTimes = retriedTimes + 1;
                var delays = TryGetIntArray(metadata, "$retryDelays") ?? _options.Value.Delays;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                DateTime? scheduleTime = null;

                if (delays.Length > 0)
                {
                    var idx = Math.Min(retriedTimes, delays.Length - 1);
                    scheduleTime = now + TimeSpan.FromSeconds(delays[idx]);
                }

                _jobContext.Metadata["$retriedTimes"] = newRetriedTimes.ToString();

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

    private static int? TryGetInt(Dictionary<string, string> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value) && int.TryParse(value, out var result))
        {
            return result;
        }

        return null;
    }

    private static int[]? TryGetIntArray(Dictionary<string, string> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value))
        {
            try
            {
                return JsonSerializer.Deserialize<int[]>(value);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }
}
