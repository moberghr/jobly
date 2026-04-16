using System.Collections.Concurrent;
using System.Reflection;
using Jobly.Core.Handlers;

namespace Jobly.Core.Mutex;

public class MutexPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    private static readonly ConcurrentDictionary<Type, MutexAttribute?> AttributeCache = new();

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        var meta = context.GetMetadata<IMutexMetadata>();

        if (meta.ConcurrencyKey == null)
        {
            var attr = AttributeCache.GetOrAdd(typeof(T), static t => t.GetCustomAttribute<MutexAttribute>());
            if (attr != null)
            {
                meta.ConcurrencyKey = attr.Key;
            }
        }

        return next();
    }
}
