using System.Diagnostics.CodeAnalysis;

namespace Jobly.Core.Handlers;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Public API")]
public delegate Task JobHandlerDelegate();

/// <summary>
/// Pipeline behavior that wraps handler execution for both IJob and IMessage types.
/// Call <paramref name="next"/> to continue the pipeline.
/// </summary>
public interface IPipelineBehavior<in T>
    where T : class
{
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Public API")]
    Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken cancellationToken);
}
