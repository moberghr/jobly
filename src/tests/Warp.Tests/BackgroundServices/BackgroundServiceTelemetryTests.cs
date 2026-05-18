using System.Diagnostics.Metrics;
using Shouldly;
using Warp.Core.Logging;

namespace Warp.Tests.BackgroundServices;

/// <summary>
/// NoDb unit tests that verify the four <see cref="WarpTelemetry"/> counters for
/// <c>WarpBackgroundService</c> fire correctly. Tests call the telemetry helpers directly —
/// no host, no DB — mirroring the <c>SagaTelemetryTests</c> style (§4.3 NoDb category).
/// </summary>
[Trait("Category", "NoDb")]
public class BackgroundServiceTelemetryTests
{
    [TimedFact]
    public void Started_ServiceEntersExecuteAsync_CounterFiresOnce()
    {
        var count = 0L;
        using var listener = StartListener("warp.background_services.started", (value, tags) =>
        {
            if (HasTag(tags, "service_name", "MyService"))
            {
                count += value;
            }
        });

        WarpTelemetry.BackgroundServicesStarted.Add(1, new KeyValuePair<string, object?>("service_name", "MyService"));

        count.ShouldBe(1);
    }

    [TimedFact]
    public void Faulted_ServiceThrows_CounterFiresWithExceptionTypeTag()
    {
        var count = 0L;
        using var listener = StartListener("warp.background_services.faulted", (value, tags) =>
        {
            if (HasTag(tags, "service_name", "FaultService")
                && HasTag(tags, "exception_type", nameof(InvalidOperationException)))
            {
                count += value;
            }
        });

        WarpTelemetry.BackgroundServicesFaulted.Add(
            1,
            new KeyValuePair<string, object?>("service_name", "FaultService"),
            new KeyValuePair<string, object?>("exception_type", nameof(InvalidOperationException)));

        count.ShouldBe(1);
    }

    [TimedFact]
    public void Faulted_DifferentExceptionType_NotCounted()
    {
        var count = 0L;
        using var listener = StartListener("warp.background_services.faulted", (value, tags) =>
        {
            if (HasTag(tags, "service_name", "FaultService")
                && HasTag(tags, "exception_type", nameof(InvalidOperationException)))
            {
                count += value;
            }
        });

        // Emit with a different exception type — should NOT be counted in our filter.
        WarpTelemetry.BackgroundServicesFaulted.Add(
            1,
            new KeyValuePair<string, object?>("service_name", "FaultService"),
            new KeyValuePair<string, object?>("exception_type", nameof(TimeoutException)));

        count.ShouldBe(0);
    }

    [TimedFact]
    public void LeaseLost_SingletonRenewalFails_CounterFiresWithServiceName()
    {
        var count = 0L;
        using var listener = StartListener("warp.background_services.lease_lost", (value, tags) =>
        {
            if (HasTag(tags, "service_name", "SingletonService"))
            {
                count += value;
            }
        });

        WarpTelemetry.BackgroundServicesLeaseLost.Add(1, new KeyValuePair<string, object?>("service_name", "SingletonService"));

        count.ShouldBe(1);
    }

    [TimedFact]
    public void Restart_SupervisorEntersBackoff_CounterFiresOnce()
    {
        var count = 0L;
        using var listener = StartListener("warp.background_services.restarts", (value, tags) =>
        {
            if (HasTag(tags, "service_name", "RestartService"))
            {
                count += value;
            }
        });

        WarpTelemetry.BackgroundServicesRestarts.Add(1, new KeyValuePair<string, object?>("service_name", "RestartService"));

        count.ShouldBe(1);
    }

    [TimedFact]
    public void MultipleIncrements_CounterAccumulates()
    {
        var count = 0L;
        using var listener = StartListener("warp.background_services.started", (value, tags) =>
        {
            if (HasTag(tags, "service_name", "LoopService"))
            {
                count += value;
            }
        });

        for (var i = 0; i < 3; i++)
        {
            WarpTelemetry.BackgroundServicesStarted.Add(1, new KeyValuePair<string, object?>("service_name", "LoopService"));
        }

        count.ShouldBe(3);
    }

    private static MeterListener StartListener(string instrumentName, Action<long, ReadOnlySpan<KeyValuePair<string, object?>>> onMeasurement)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (string.Equals(instrument.Meter.Name, "Warp", StringComparison.Ordinal)
                    && string.Equals(instrument.Name, instrumentName, StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => onMeasurement(value, tags));
        listener.Start();

        return listener;
    }

    private static bool HasTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key, string value)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal) && Equals(tag.Value, value))
            {
                return true;
            }
        }

        return false;
    }
}
