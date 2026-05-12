using Warp.Core.Handlers;

namespace Warp.Core.RateLimit;

public partial interface IRateLimitMetadata : IJobMetadata
{
    string? RateLimitKey { get; set; }

    int? RateLimitCount { get; set; }

    int? RateLimitWindowSeconds { get; set; }

    // Property names are prefixed with the addon namespace because all IXxxMetadata interfaces
    // share a single backing Dictionary<string, object> per job. A bare "Mode" or "Style" would
    // collide silently with IConcurrencyMetadata.Mode (or a future addon's Style) and the publish
    // behaviours would overwrite each other's values. See feedback_metadata_dict_shadowing.md.
    RateLimitMode? RateLimitMode { get; set; }

    RateLimitStyle? RateLimitStyle { get; set; }
}
