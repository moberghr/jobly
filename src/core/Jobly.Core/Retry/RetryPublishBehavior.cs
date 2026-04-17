using Jobly.Core.Handlers;
using Microsoft.Extensions.Options;

namespace Jobly.Core.Retry;

public class RetryPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    private readonly IOptions<RetryOptions> _options;

    public RetryPublishBehavior(IOptions<RetryOptions> options)
    {
        _options = options;
    }

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        var meta = context.GetMetadata<IRetryMetadata>();

        meta.MaxRetries ??= _options.Value.MaxRetries;

        if (_options.Value.Delays.Length > 0 && meta.RetryDelays == null)
        {
            meta.RetryDelays = _options.Value.Delays;
        }

        return next();
    }
}
