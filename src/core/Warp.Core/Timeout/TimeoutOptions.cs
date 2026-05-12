namespace Warp.Core.Timeout;

public class TimeoutOptions
{
    public TimeSpan? Default { get; set; }

    public TimeoutMode DefaultMode { get; set; } = TimeoutMode.Delete;

    public TimeoutScope DefaultScope { get; set; } = TimeoutScope.PerAttempt;
}
