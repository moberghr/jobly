using Warp.Core.Handlers;

namespace Warp.Core.Timeout;

public partial interface ITimeoutMetadata : IJobMetadata
{
    int? TimeoutSeconds { get; set; }

    // Property names are addon-namespaced so all metadata keys live in disjoint slots of the
    // shared Dictionary<string, object> backing every job — a job carrying [Mutex] + WithTimeout
    // must not collide on a generic "Mode" / "Scope" key with IConcurrencyMetadata.Mode etc.
    TimeoutMode? TimeoutMode { get; set; }

    TimeoutScope? TimeoutScope { get; set; }

    DateTime? TimeoutDeadlineUtc { get; set; }
}
