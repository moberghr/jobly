using Warp.Core.Helper;

namespace Warp.Core.Mutex;

public static class MutexExtensions
{
    public static JobParameters WithMutex(this JobParameters parameters, string key, MutexMode mode = MutexMode.Skip)
    {
        parameters.Configure<IMutexMetadata>(x =>
        {
            x.ConcurrencyKey = key;
            x.Mode = mode;
        });

        return parameters;
    }
}
