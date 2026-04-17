using Jobly.Core.Handlers;

namespace Jobly.Core.Mutex;

public partial interface IMutexMetadata : IJobMetadata
{
    string? ConcurrencyKey { get; set; }
}
