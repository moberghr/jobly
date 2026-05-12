using Warp.Core.Helper;

namespace Warp.Core.RateLimit;

public static class RateLimitExtensions
{
    public static JobParameters WithRateLimit(
        this JobParameters parameters,
        string key,
        int count,
        TimeSpan window,
        RateLimitMode mode = RateLimitMode.Skip,
        RateLimitStyle style = RateLimitStyle.Fixed)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        parameters.Configure<IRateLimitMetadata>(x =>
        {
            x.RateLimitKey = key;
            x.RateLimitCount = count;
            x.RateLimitWindowSeconds = (int)Math.Ceiling(window.TotalSeconds);
            x.RateLimitMode = mode;
            x.RateLimitStyle = style;
        });

        return parameters;
    }
}
