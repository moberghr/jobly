namespace Warp.Core.Timeout;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TimeoutAttribute : Attribute
{
    public TimeoutAttribute(int seconds)
    {
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Timeout must be positive.");
        }

        Seconds = seconds;
    }

    public int Seconds { get; }

    public TimeoutMode Mode { get; init; } = TimeoutMode.Delete;

    public TimeoutScope Scope { get; init; } = TimeoutScope.PerAttempt;
}
