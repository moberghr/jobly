namespace Warp.Core.BackgroundServices;

/// <summary>
/// Aggregated summary for one background service name, returned by
/// <see cref="IBackgroundServiceQueryService.ListAsync"/>.
/// </summary>
public sealed class BackgroundServiceListItemDto
{
    public string Name { get; init; } = string.Empty;

    public ServiceScope Scope { get; init; }

    /// <summary>Instances currently in <c>Running</c> status.</summary>
    public int RunningCount { get; init; }

    /// <summary>Instances currently in <c>Waiting</c> status (singleton only; always 0 for per-server).</summary>
    public int WaitingCount { get; init; }

    /// <summary>Instances currently in <c>Faulted</c> status.</summary>
    public int FaultedCount { get; init; }

    /// <summary>Instances currently in <c>ConfigurationMismatch</c> status.</summary>
    public int ConfigurationMismatchCount { get; init; }

    public int TotalInstances { get; init; }

    /// <summary>Sum of <c>RestartCount</c> across all instances.</summary>
    public int TotalRestartCount { get; init; }

    /// <summary>
    /// Exception type extracted from the most-recent faulted instance's <c>LastError</c> field.
    /// Null when no instance is currently in <c>Faulted</c> status.
    /// </summary>
    public string? LastErrorType { get; init; }
}
