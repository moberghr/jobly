using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Options;
using Warp.Core.Handlers;

namespace Warp.Core.Timeout;

public class TimeoutPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    private static readonly ConcurrentDictionary<Type, TimeoutAttribute?> AttributeCache = new();

    private readonly IOptions<TimeoutOptions> _options;
    private readonly TimeProvider _timeProvider;

    public TimeoutPublishBehavior(IOptions<TimeoutOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        var meta = context.GetMetadata<ITimeoutMetadata>();

        if (meta.TimeoutSeconds == null)
        {
            var attr = AttributeCache.GetOrAdd(typeof(T), static t => t.GetCustomAttribute<TimeoutAttribute>());
            if (attr != null)
            {
                meta.TimeoutSeconds = attr.Seconds;
                meta.TimeoutMode ??= attr.Mode;
                meta.TimeoutScope ??= attr.Scope;
            }
            else if (_options.Value.Default is { } def)
            {
                meta.TimeoutSeconds = (int)Math.Ceiling(def.TotalSeconds);
                meta.TimeoutMode ??= _options.Value.DefaultMode;
                meta.TimeoutScope ??= _options.Value.DefaultScope;
            }
        }

        if (meta.TimeoutSeconds is { } secs
            && meta.TimeoutScope == TimeoutScope.Total
            && meta.TimeoutDeadlineUtc == null)
        {
            meta.TimeoutDeadlineUtc = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(secs);
        }

        return next();
    }
}
