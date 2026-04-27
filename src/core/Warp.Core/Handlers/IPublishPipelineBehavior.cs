using System.Diagnostics.CodeAnalysis;

namespace Warp.Core.Handlers;

public delegate Task PublishDelegate();

public class PublishContext<T>
{
    public required T Job { get; init; }

    public Dictionary<string, object> Metadata { get; set; } = [];

    public TMeta GetMetadata<TMeta>()
        where TMeta : class, IJobMetadata
    {
        var typed = MetadataFactory.Create<TMeta>(Metadata);
        Metadata = (Dictionary<string, object>)(object)typed;

        return typed;
    }
}

[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Public API")]
public interface IPublishPipelineBehavior<T>
{
    Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct);
}
