using Warp.Core.Helper;

namespace Warp.Core.Concurrency;

public static class ConcurrencyExtensions
{
    public static JobParameters WithMutex(this JobParameters parameters, string key, ConcurrencyMode mode = ConcurrencyMode.Skip)
    {
        parameters.Configure<IConcurrencyMetadata>(x =>
        {
            x.ConcurrencyKey = key;
            x.Limit = 1;
            x.Mode = mode;
        });

        return parameters;
    }

    public static JobParameters WithSemaphore(this JobParameters parameters, string key, int limit, ConcurrencyMode mode = ConcurrencyMode.Wait)
    {
        parameters.Configure<IConcurrencyMetadata>(x =>
        {
            x.ConcurrencyKey = key;
            x.Limit = limit;
            x.Mode = mode;
        });

        return parameters;
    }
}
