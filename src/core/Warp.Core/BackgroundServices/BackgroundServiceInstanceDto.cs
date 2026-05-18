namespace Warp.Core.BackgroundServices;

/// <summary>
/// Per-instance detail, embedded in <see cref="BackgroundServiceDetailDto"/>.
/// </summary>
public sealed class BackgroundServiceInstanceDto
{
    public Guid ServerId { get; init; }

    /// <summary>
    /// Human-readable name from the <c>Server</c> row (<c>ServerName</c>). Null when the
    /// Server row is missing (rare — typically a race between an Instance row's lifecycle
    /// and ServerCleanup).
    /// </summary>
    public string? ServerName { get; init; }

    public string ServiceName { get; init; } = string.Empty;

    public ServiceScope DeclaredScope { get; init; }

    public BackgroundServiceStatus Status { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime LastHeartbeatAt { get; init; }

    public string? LastError { get; init; }

    public DateTime? LastErrorAt { get; init; }

    public int RestartCount { get; init; }
}
