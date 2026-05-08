using System.Collections.Concurrent;
using System.Reflection;
using Warp.Core.Handlers;

namespace Warp.Core.Concurrency;

public class ConcurrencyPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    private static readonly ConcurrentDictionary<Type, ConcurrencyAttributes> AttributeCache = new();

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        var meta = context.GetMetadata<IConcurrencyMetadata>();

        if (meta.ConcurrencyKey == null)
        {
            var attrs = AttributeCache.GetOrAdd(typeof(T), static t => new ConcurrencyAttributes(
                t.GetCustomAttribute<MutexAttribute>(),
                t.GetCustomAttribute<SemaphoreAttribute>()));

            if (attrs.Mutex != null)
            {
                meta.ConcurrencyKey = attrs.Mutex.Key;
                meta.Limit = 1;
                meta.Mode = attrs.Mutex.Mode;
            }
            else if (attrs.Semaphore != null)
            {
                meta.ConcurrencyKey = attrs.Semaphore.Key;
                meta.Limit = attrs.Semaphore.Limit;
                meta.Mode = attrs.Semaphore.Mode;
            }
        }

        return next();
    }

    private sealed record ConcurrencyAttributes(MutexAttribute? Mutex, SemaphoreAttribute? Semaphore);
}
