using Jobly.Core.Handlers;

namespace Jobly.Worker.Retry;

public partial interface IRetryMetadata : IJobMetadata
{
    int? MaxRetries { get; set; }

    int RetriedTimes { get; set; }

    int[]? RetryDelays { get; set; }
}
