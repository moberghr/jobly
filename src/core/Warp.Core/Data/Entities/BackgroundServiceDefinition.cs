using Warp.Core.BackgroundServices;

namespace Warp.Core.Data.Entities;

/// <summary>
/// One row per service <c>Name</c> across the cluster. Created on first registration and
/// retained indefinitely as an audit record. Owns the authoritative <c>DeclaredScope</c>
/// against which all instances compare their own declared scope; any mismatch → the
/// instance sets <c>Status = ConfigurationMismatch</c> and refuses to start.
/// </summary>
public class BackgroundServiceDefinition
{
    public string Name { get; set; } = string.Empty;

    public ServiceScope DeclaredScope { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }
}
