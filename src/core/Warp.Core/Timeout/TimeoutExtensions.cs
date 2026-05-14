using Warp.Core.Helper;

namespace Warp.Core.Timeout;

public static class TimeoutExtensions
{
    public static JobParameters WithTimeout(
        this JobParameters parameters,
        TimeSpan timeout,
        TimeoutMode mode = TimeoutMode.Delete,
        TimeoutScope scope = TimeoutScope.PerAttempt)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive.");
        }

        parameters.Configure<ITimeoutMetadata>(x =>
        {
            x.TimeoutSeconds = (int)Math.Ceiling(timeout.TotalSeconds);
            x.TimeoutMode = mode;
            x.TimeoutScope = scope;
        });

        return parameters;
    }
}
