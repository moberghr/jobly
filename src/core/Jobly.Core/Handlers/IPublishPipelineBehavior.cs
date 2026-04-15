using System.Diagnostics.CodeAnalysis;

namespace Jobly.Core.Handlers;

public delegate Task PublishDelegate();

public class PublishContext<T>
{
    public required T Job { get; init; }

    public Dictionary<string, object> Metadata { get; init; } = [];
}

[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Public API")]
public interface IPublishPipelineBehavior<T>
{
    Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct);
}
