using Jobly.Worker;
using Shouldly;

namespace Jobly.Tests.Unit;

public class PollingBackoffTests
{
    private static readonly TimeSpan Floor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(30);

    [TimedFact]
    public void Next_FactorOne_ReturnsFloor()
    {
        var result = PollingBackoff.Next(TimeSpan.FromSeconds(5), Floor, Max, 1.0);

        result.ShouldBe(Floor);
    }

    [TimedFact]
    public void Next_FactorBelowOne_ReturnsFloor()
    {
        var result = PollingBackoff.Next(TimeSpan.FromSeconds(5), Floor, Max, 0.5);

        result.ShouldBe(Floor);
    }

    [TimedFact]
    public void Next_CurrentBelowFloor_StartsAtFloor()
    {
        var result = PollingBackoff.Next(TimeSpan.Zero, Floor, Max, 2.0);

        result.ShouldBe(Floor);
    }

    [TimedFact]
    public void Next_FirstMiss_DoubledFromFloor()
    {
        var result = PollingBackoff.Next(Floor, Floor, Max, 2.0);

        result.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [TimedFact]
    public void Next_GrowsGeometrically()
    {
        var current = Floor;
        var expected = new[]
        {
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(16),
            TimeSpan.FromSeconds(30),
        };

        foreach (var step in expected)
        {
            current = PollingBackoff.Next(current, Floor, Max, 2.0);
            current.ShouldBe(step);
        }
    }

    [TimedFact]
    public void Next_ClampedAtMax()
    {
        var result = PollingBackoff.Next(TimeSpan.FromSeconds(20), Floor, Max, 2.0);

        result.ShouldBe(Max);
    }

    [TimedFact]
    public void Next_AlreadyAtMax_StaysAtMax()
    {
        var result = PollingBackoff.Next(Max, Floor, Max, 2.0);

        result.ShouldBe(Max);
    }

    [TimedFact]
    public void Next_CustomFactor_ComputesCorrectly()
    {
        var result = PollingBackoff.Next(Floor, Floor, Max, 1.5);

        result.ShouldBe(TimeSpan.FromMilliseconds(1500));
    }

    [TimedFact]
    public void Next_FractionalSeconds_HandledViaTicks()
    {
        var current = TimeSpan.FromMilliseconds(100);
        var floor = TimeSpan.FromMilliseconds(50);

        var result = PollingBackoff.Next(current, floor, Max, 2.0);

        result.ShouldBe(TimeSpan.FromMilliseconds(200));
    }

    [TimedFact]
    public void Next_FactorNaN_ReturnsFloor()
    {
        var result = PollingBackoff.Next(TimeSpan.FromSeconds(5), Floor, Max, double.NaN);

        result.ShouldBe(Floor);
    }

    [TimedFact]
    public void Next_FactorPositiveInfinity_ReturnsFloor()
    {
        var result = PollingBackoff.Next(TimeSpan.FromSeconds(5), Floor, Max, double.PositiveInfinity);

        result.ShouldBe(Floor);
    }

    [TimedFact]
    public void Next_MaxBelowFloor_ReturnsFloor()
    {
        var misconfiguredMax = TimeSpan.FromMilliseconds(500);

        var result = PollingBackoff.Next(TimeSpan.FromSeconds(5), Floor, misconfiguredMax, 2.0);

        result.ShouldBe(Floor);
    }
}
