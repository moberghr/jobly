using Warp.Core.Handlers;

namespace Warp.Core.Concurrency;

public partial interface IConcurrencyMetadata : IJobMetadata
{
    string? ConcurrencyKey { get; set; }

    int? Limit { get; set; }

    ConcurrencyMode? Mode { get; set; }
}
