using System.Text.Json;
using Jobly.Core.Handlers;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Retry;

public class RetryPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    private readonly IOptions<RetryOptions> _options;

    public RetryPublishBehavior(IOptions<RetryOptions> options)
    {
        _options = options;
    }

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        if (!context.Metadata.ContainsKey("$maxRetries"))
        {
            context.Metadata["$maxRetries"] = _options.Value.MaxRetries.ToString();
        }

        if (_options.Value.Delays.Length > 0 && !context.Metadata.ContainsKey("$retryDelays"))
        {
            context.Metadata["$retryDelays"] = JsonSerializer.Serialize(_options.Value.Delays);
        }

        return next();
    }
}
