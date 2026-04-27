using Warp.Core.Handlers;

namespace Warp.Core.Mutex;

public partial interface IMutexMetadata : IJobMetadata
{
    string? ConcurrencyKey { get; set; }
}
