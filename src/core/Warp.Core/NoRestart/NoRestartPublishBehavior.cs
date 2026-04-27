using System.Reflection;
using Warp.Core.Handlers;

namespace Warp.Core.NoRestart;

public class NoRestartPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    // Static on a generic type ⇒ one cached value per closed T. A normal (non-throwing)
    // classification is cached after the first successful call. PublicationOnly ensures
    // the throw for the both-attributes conflict is NOT cached — the factory keeps running
    // on every subsequent Value access until the attribute conflict is resolved at the
    // source (though the conflict can only be resolved by a code change + recompile).
    private static readonly Lazy<bool?> AttributeCache =
        new(ClassifyFromAttributes, LazyThreadSafetyMode.PublicationOnly);

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        var meta = context.GetMetadata<ICanBeRestartedMetadata>();

        if (meta.CanBeRestarted == null)
        {
            var fromAttribute = AttributeCache.Value;
            if (fromAttribute != null)
            {
                meta.CanBeRestarted = fromAttribute;
            }
        }

        return next();
    }

    private static bool? ClassifyFromAttributes()
    {
        var t = typeof(T);

        // Prefer the closest declaration in the inheritance chain: if T directly declares
        // either attribute, use that and ignore any from base types. Without this, a derived
        // class with [NoRestart] that inherits a base's [Restart] would trip the
        // "both attributes" diagnostic and fail to override.
        var directNoRestart = t.IsDefined(typeof(NoRestartAttribute), inherit: false);
        var directRestart = t.IsDefined(typeof(RestartAttribute), inherit: false);

        if (directNoRestart && directRestart)
        {
            throw new InvalidOperationException(
                $"Type '{t.FullName}' has both [NoRestart] and [Restart] attributes. Choose one.");
        }

        if (directNoRestart)
        {
            return false;
        }

        if (directRestart)
        {
            return true;
        }

        // No direct declaration — inherit from the closest base that declares either.
        var inheritedNoRestart = t.GetCustomAttribute<NoRestartAttribute>() != null;
        var inheritedRestart = t.GetCustomAttribute<RestartAttribute>() != null;

        if (inheritedNoRestart && inheritedRestart)
        {
            throw new InvalidOperationException(
                $"Type '{t.FullName}' inherits both [NoRestart] and [Restart] attributes through its base chain. Add the intended attribute directly on the type to disambiguate.");
        }

        if (inheritedNoRestart)
        {
            return false;
        }

        if (inheritedRestart)
        {
            return true;
        }

        return null;
    }
}
