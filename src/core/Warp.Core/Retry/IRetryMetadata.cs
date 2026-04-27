using Warp.Core.Handlers;

namespace Warp.Core.Retry;

public partial interface IRetryMetadata : IJobMetadata
{
    int? MaxRetries { get; set; }

    int RetriedTimes { get; set; }

    int[]? RetryDelays { get; set; }
}
