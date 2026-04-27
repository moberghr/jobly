using Warp.Core.Helper;

namespace Warp.Core.Retry;

public static class RetryExtensions
{
    public static JobParameters WithRetry(this JobParameters parameters, int maxRetries, int[]? delays = null)
    {
        parameters.Configure<IRetryMetadata>(x =>
        {
            x.MaxRetries = maxRetries;
            if (delays != null)
            {
                x.RetryDelays = delays;
            }
        });

        return parameters;
    }
}
