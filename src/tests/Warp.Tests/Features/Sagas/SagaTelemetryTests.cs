using System.Diagnostics.Metrics;
using Shouldly;
using Warp.Core.Handlers;
using Warp.Core.Sagas;
using Warp.Tests.Fixtures;

namespace Warp.Tests.Features.Sagas;

[Trait("Category", "NoDb")]
public class SagaTelemetryTests
{
    [TimedFact]
    public async Task NewSaga_FromStartsSaga_IncrementsSagasStarted()
    {
        var count = 0L;
        using var listener = StartListener("warp.sagas.started", (value, tags) =>
        {
            if (HasTag(tags, "saga_type", nameof(TelemetrySaga)))
            {
                count += value;
            }
        });

        var (store, semaphore, jobContext, cache, time) = SetUp();
        var proxy = new SagaHandlerProxy<TelemetrySaga, StartTelemetry>(
            new TelemetryHandler(), store, semaphore, jobContext, time, cache);

        await proxy.HandleAsync(new StartTelemetry { CorrelationKey = "started" }, CancellationToken.None);

        count.ShouldBe(1);
    }

    [TimedFact]
    public async Task ExistingSagaCompletes_IncrementsSagasCompleted()
    {
        var count = 0L;
        using var listener = StartListener("warp.sagas.completed", (value, tags) =>
        {
            if (HasTag(tags, "saga_type", nameof(TelemetrySaga)))
            {
                count += value;
            }
        });

        var (store, semaphore, jobContext, cache, time) = SetUp();
        store.Seed("done", new TelemetrySaga { CorrelationKey = "done" });
        var proxy = new SagaHandlerProxy<TelemetrySaga, CompleteTelemetry>(
            new TelemetryHandler(), store, semaphore, jobContext, time, cache);

        await proxy.HandleAsync(new CompleteTelemetry { CorrelationKey = "done" }, CancellationToken.None);

        count.ShouldBe(1);
    }

    [TimedFact]
    public async Task MutexBusy_IncrementsSagasRequeued_WithBusyReason()
    {
        var count = 0L;
        using var listener = StartListener("warp.sagas.requeued", (value, tags) =>
        {
            if (HasTag(tags, "saga_type", nameof(TelemetrySaga)) && HasTag(tags, "reason", "busy"))
            {
                count += value;
            }
        });

        var (store, semaphore, jobContext, cache, time) = SetUp();
        await using var holder = semaphore.HoldSlot($"warp:saga:{typeof(TelemetrySaga).FullName}:contended", 1);
        var proxy = new SagaHandlerProxy<TelemetrySaga, CompleteTelemetry>(
            new TelemetryHandler(), store, semaphore, jobContext, time, cache);

        await proxy.HandleAsync(new CompleteTelemetry { CorrelationKey = "contended" }, CancellationToken.None);

        count.ShouldBe(1);
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

    private static (FakeSagaStore store, FakeSemaphoreProvider semaphore, JobContext jobContext, SagaCorrelationCache cache, TimeProvider time) SetUp()
    {
        return (new FakeSagaStore(), new FakeSemaphoreProvider(), new JobContext(), new SagaCorrelationCache(), TimeProvider.System);
    }

    public sealed class TelemetrySaga : Saga;

    [StartsSaga]
    public sealed class StartTelemetry : IMessage
    {
        [Correlate]
        public string CorrelationKey { get; set; } = string.Empty;
    }

    public sealed class CompleteTelemetry : IMessage
    {
        [Correlate]
        public string CorrelationKey { get; set; } = string.Empty;
    }

    private sealed class TelemetryHandler :
        ISagaHandler<TelemetrySaga, StartTelemetry>,
        ISagaHandler<TelemetrySaga, CompleteTelemetry>
    {
        public Task HandleAsync(TelemetrySaga saga, StartTelemetry message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleAsync(TelemetrySaga saga, CompleteTelemetry message, CancellationToken cancellationToken)
        {
            saga.MarkCompleted();
            return Task.CompletedTask;
        }
    }
}
