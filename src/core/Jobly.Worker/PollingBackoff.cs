namespace Jobly.Worker;

internal static class PollingBackoff
{
    public static TimeSpan Next(TimeSpan current, TimeSpan floor, TimeSpan max, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 1.0)
        {
            return floor;
        }

        if (max < floor)
        {
            return floor;
        }

        if (current < floor)
        {
            return floor;
        }

        var ticks = (long)(current.Ticks * factor);
        if (ticks > max.Ticks)
        {
            ticks = max.Ticks;
        }

        return TimeSpan.FromTicks(ticks);
    }
}
