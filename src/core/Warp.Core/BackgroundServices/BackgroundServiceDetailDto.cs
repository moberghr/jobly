namespace Warp.Core.BackgroundServices;

/// <summary>
/// Full detail for a single background service name, returned by
/// <see cref="IBackgroundServiceQueryService.GetAsync"/>.
/// </summary>
public sealed class BackgroundServiceDetailDto
{
    public string Name { get; init; } = string.Empty;

    public ServiceScope DeclaredScope { get; init; }

    public DateTime FirstSeenAt { get; init; }

    public DateTime LastSeenAt { get; init; }

    public IReadOnlyList<BackgroundServiceInstanceDto> Instances { get; init; } = [];
}
