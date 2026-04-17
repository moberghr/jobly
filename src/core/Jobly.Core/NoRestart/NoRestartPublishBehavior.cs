using System.Collections.Concurrent;
using System.Reflection;
using Jobly.Core.Handlers;

namespace Jobly.Core.NoRestart;

public class NoRestartPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    // Static on a generic type ⇒ one dictionary per closed T (per-type cache, not shared across Ts).
    // When both attributes are present the factory throws; GetOrAdd does not cache exceptions,
    // so the throw repeats on every publish until the attribute conflict is resolved.
    private static readonly ConcurrentDictionary<Type, bool?> AttributeCache = new();

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        var meta = context.GetMetadata<ICanBeRestartedMetadata>();

        if (meta.CanBeRestarted == null)
        {
            var fromAttribute = AttributeCache.GetOrAdd(typeof(T), static t =>
            {
                var hasNoRestart = t.GetCustomAttribute<NoRestartAttribute>() != null;
                var hasRestart = t.GetCustomAttribute<RestartAttribute>() != null;

                if (hasNoRestart && hasRestart)
                {
                    throw new InvalidOperationException(
                        $"Type '{t.FullName}' has both [NoRestart] and [Restart] attributes. Choose one.");
                }

                if (hasNoRestart)
                {
                    return false;
                }

                if (hasRestart)
                {
                    return true;
                }

                return null;
            });

            if (fromAttribute != null)
            {
                meta.CanBeRestarted = fromAttribute;
            }
        }

        return next();
    }
}
