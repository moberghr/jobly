using Warp.Core.Helper;

namespace Warp.Core.Mutex;

public static class MutexExtensions
{
    public static JobParameters WithMutex(this JobParameters parameters, string key)
    {
        parameters.Configure<IMutexMetadata>(x => x.ConcurrencyKey = key);

        return parameters;
    }
}
