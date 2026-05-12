using System.Collections.Concurrent;
using System.Reflection;
using Warp.Core.Handlers;

namespace Warp.Core.RateLimit;

public class RateLimitPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    private static readonly ConcurrentDictionary<Type, RateLimitAttribute?> AttributeCache = new();

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        var meta = context.GetMetadata<IRateLimitMetadata>();

        if (meta.RateLimitKey == null)
        {
            var attr = AttributeCache.GetOrAdd(typeof(T), static t => t.GetCustomAttribute<RateLimitAttribute>());

            if (attr != null)
            {
                meta.RateLimitKey = attr.Key;
                meta.RateLimitCount = attr.Count;
                meta.RateLimitWindowSeconds = attr.PerSeconds;
                meta.RateLimitMode = attr.Mode;
                meta.RateLimitStyle = attr.Style;
            }
        }

        return next();
    }
}
