using Warp.Core.Handlers;

namespace Warp.Core.Concurrency;

public partial interface IConcurrencyMetadata : IJobMetadata
{
    string? ConcurrencyKey { get; set; }

    // Property names are addon-prefixed because all IXxxMetadata interfaces share a single
    // backing Dictionary<string, object> per job. Bare "Limit" or "Mode" would collide silently
    // with any other addon that exposes the same names, and the publish behaviours would
    // overwrite each other's values. See feedback_metadata_dict_shadowing.md.
    int? ConcurrencyLimit { get; set; }

    ConcurrencyMode? ConcurrencyMode { get; set; }
}
