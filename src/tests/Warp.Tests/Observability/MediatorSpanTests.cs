using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core.Handlers;
using Warp.Core.Logging;
using Warp.Tests.Helpers;

namespace Warp.Tests.Observability;

/// <summary>
/// Pure unit tests for IMediator.Send / IMediator.CreateStream OTel instrumentation —
/// no database, no fixture. Asserts span name, kind, tags, and the
/// warp.mediator.duration / warp.mediator.in_flight metrics.
/// </summary>
[Trait("Category", "NoDb")]
[Collection("Telemetry")]
public class MediatorSpanTests
{
    private static Mediator BuildMediator(Action<IServiceCollection>? configure = null)
    {
        // Use the reflection-based Mediator (not the generated one) so the test only registers
        // the handlers it actually uses — the generated mediator's eager wrapper creation
        // would force every IRequest<T> in the assembly to have a registered handler.
        var services = new ServiceCollection();
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();

        return new Mediator(provider);
    }

    public sealed class MedSpanEcho : IRequest<string>
    {
        public string Value { get; set; } = string.Empty;
    }

    public sealed class EchoHandler : IRequestHandler<MedSpanEcho, string>
    {
        public Task<string> HandleAsync(MedSpanEcho request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(request.Value);
        }
    }

    public sealed class FailingRequest : IRequest<string>;

    public sealed class FailingRequestException : Exception
    {
        public FailingRequestException()
        {
        }

        public FailingRequestException(string message)
            : base(message)
        {
        }

        public FailingRequestException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public sealed class FailingHandler : IRequestHandler<FailingRequest, string>
    {
        public Task<string> HandleAsync(FailingRequest request, CancellationToken cancellationToken)
            => throw new FailingRequestException();
    }

    public sealed class CountStream : IStreamRequest<int>
    {
        public int Count { get; set; }
    }

    public sealed class CountStreamHandler : IStreamRequestHandler<CountStream, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(CountStream request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < request.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return i;
                await Task.Yield();
            }
        }
    }

    [TimedFact]
    public async Task Send_HappyPath_EmitsProcessSpanWithMediatorTags()
    {
        using var harness = new ActivityListenerHarness();
        var mediator = BuildMediator(s => s.AddTransient<IRequestHandler<MedSpanEcho, string>, EchoHandler>());

        var result = await mediator.Send(new MedSpanEcho { Value = "hello" });

        result.ShouldBe("hello");
        var span = harness.FirstByName("process MedSpanEcho");
        span.ShouldNotBeNull();
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.GetTagItem(WarpTelemetryAttributes.MessagingSystem).ShouldBe(WarpTelemetryAttributes.MessagingSystemValue);
        span.GetTagItem(WarpTelemetryAttributes.MessagingOperationName).ShouldBe(WarpTelemetryAttributes.OperationProcess);
        span.GetTagItem(WarpTelemetryAttributes.MessagingDestinationName).ShouldBe("MedSpanEcho");
        span.GetTagItem(WarpTelemetryAttributes.WarpMediatorKind).ShouldBe(WarpTelemetryAttributes.MediatorKindRequest);
        span.GetTagItem(WarpTelemetryAttributes.WarpMediatorResponseType).ShouldBe("String");
    }

    [TimedFact]
    public async Task Send_HandlerThrows_SpanRecordsErrorTypeAndStatusFailedHistogram()
    {
        using var harness = new ActivityListenerHarness();
        using var meterRecorder = new MeterRecorder("warp.mediator.duration");
        var mediator = BuildMediator(s => s.AddTransient<IRequestHandler<FailingRequest, string>, FailingHandler>());

        await Should.ThrowAsync<FailingRequestException>(() => mediator.Send(new FailingRequest()));

        var span = harness.FirstByName("process FailingRequest");
        span.ShouldNotBeNull();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.GetTagItem(WarpTelemetryAttributes.ErrorType).ShouldBe(typeof(FailingRequestException).FullName);

        meterRecorder.HasMeasurementWithTagValue("status", "failed").ShouldBeTrue();
    }

    [TimedFact]
    public async Task Send_Cancelled_HistogramRecordsStatusCancelled()
    {
        using var meterRecorder = new MeterRecorder("warp.mediator.duration");
        var mediator = BuildMediator(s => s.AddTransient<IRequestHandler<MedSpanEcho, string>, EchoHandler>());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            mediator.Send(new MedSpanEcho { Value = "x" }, cts.Token));

        meterRecorder.HasMeasurementWithTagValue("status", "cancelled").ShouldBeTrue();
    }

    [TimedFact]
    public async Task CreateStream_HappyPath_SpanLivesAcrossEnumeration()
    {
        using var harness = new ActivityListenerHarness();
        var mediator = BuildMediator(s => s.AddTransient<IStreamRequestHandler<CountStream, int>, CountStreamHandler>());

        var collected = new List<int>();
        await foreach (var item in mediator.CreateStream(new CountStream { Count = 5 }))
        {
            collected.Add(item);
        }

        collected.ShouldBe([0, 1, 2, 3, 4]);
        var span = harness.FirstByName("process CountStream");
        span.ShouldNotBeNull();
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.GetTagItem(WarpTelemetryAttributes.WarpMediatorKind).ShouldBe(WarpTelemetryAttributes.MediatorKindStream);
        span.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    public sealed class UnregisteredRequest : IRequest<string>;

    [TimedFact]
    public async Task Send_NoHandlerRegistered_RecordsErrorTypeAndStatusFailed()
    {
        using var harness = new ActivityListenerHarness();
        using var meterRecorder = new MeterRecorder("warp.mediator.duration");
        var mediator = BuildMediator();

        await Should.ThrowAsync<InvalidOperationException>(() => mediator.Send(new UnregisteredRequest()));

        var span = harness.FirstByName("process UnregisteredRequest");
        span.ShouldNotBeNull();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.GetTagItem(WarpTelemetryAttributes.ErrorType).ShouldBe(typeof(InvalidOperationException).FullName);
        meterRecorder.HasMeasurementWithTagValue("status", "failed").ShouldBeTrue();
    }

    [TimedFact]
    public async Task CreateStream_Cancelled_HistogramRecordsStatusCancelled()
    {
        using var meterRecorder = new MeterRecorder("warp.mediator.duration");
        var mediator = BuildMediator(s => s.AddTransient<IStreamRequestHandler<CountStream, int>, CountStreamHandler>());

        using var cts = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in mediator.CreateStream(new CountStream { Count = 100 }, cts.Token))
            {
                if (item >= 1)
                {
                    // Cancel mid-stream — the next MoveNextAsync inside InstrumentStream observes
                    // cancellationToken.IsCancellationRequested and routes to the OperationCanceledException
                    // catch, which sets status="cancelled".
                    await cts.CancelAsync();
                }
            }
        });

        meterRecorder.HasMeasurementWithTagValue("status", "cancelled").ShouldBeTrue();
    }

    public sealed class FailingStream : IStreamRequest<int>;

    public sealed class FailingStreamHandler : IStreamRequestHandler<FailingStream, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(FailingStream request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return 1;
            await Task.Yield();

            throw new FailingRequestException();
        }
    }

    [TimedFact]
    public async Task CreateStream_HandlerThrows_RecordsErrorTypeAndStatusFailed()
    {
        using var harness = new ActivityListenerHarness();
        using var meterRecorder = new MeterRecorder("warp.mediator.duration");
        var mediator = BuildMediator(s => s.AddTransient<IStreamRequestHandler<FailingStream, int>, FailingStreamHandler>());

        await Should.ThrowAsync<FailingRequestException>(async () =>
        {
            await foreach (var item in mediator.CreateStream(new FailingStream()))
            {
                _ = item;
            }
        });

        var span = harness.FirstByName("process FailingStream");
        span.ShouldNotBeNull();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.GetTagItem(WarpTelemetryAttributes.ErrorType).ShouldBe(typeof(FailingRequestException).FullName);
        meterRecorder.HasMeasurementWithTagValue("status", "failed").ShouldBeTrue();
    }

    [TimedFact]
    public async Task CreateStream_EarlyBreak_SpanIsClosedAndStatusSucceeded()
    {
        using var harness = new ActivityListenerHarness();
        using var meterRecorder = new MeterRecorder("warp.mediator.duration");

        // Filter to this test's request type — the in_flight UpDownCounter is process-wide
        // and other parallel tests using a different request type would otherwise contaminate
        // the running sum.
        using var counterRecorder = new UpDownRecorder("warp.mediator.in_flight", "request_type", "CountStream");
        var mediator = BuildMediator(s => s.AddTransient<IStreamRequestHandler<CountStream, int>, CountStreamHandler>());

        await foreach (var item in mediator.CreateStream(new CountStream { Count = 100 }))
        {
            if (item >= 2)
            {
                break;
            }
        }

        // Span closed via the iterator's finally even on early break. Status must NOT be Error
        // (no exception, no cancellation), histogram must record status=succeeded, and the
        // in-flight gauge must have decremented back to zero.
        var span = harness.FirstByName("process CountStream");
        span.ShouldNotBeNull();
        span.Status.ShouldNotBe(ActivityStatusCode.Error);
        meterRecorder.HasMeasurementWithTagValue("status", "succeeded").ShouldBeTrue();
        counterRecorder.Sum.ShouldBe(0);
    }

    [TimedFact]
    public async Task Send_HappyPath_RecordsDurationHistogramWithStatusSucceeded()
    {
        using var meterRecorder = new MeterRecorder("warp.mediator.duration");
        var mediator = BuildMediator(s => s.AddTransient<IRequestHandler<MedSpanEcho, string>, EchoHandler>());

        await mediator.Send(new MedSpanEcho { Value = "x" });

        meterRecorder.HasMeasurementWithTagValue("status", "succeeded").ShouldBeTrue();
        meterRecorder.HasMeasurementWithTagValue("kind", WarpTelemetryAttributes.MediatorKindRequest).ShouldBeTrue();
        meterRecorder.HasMeasurementWithTagValue("request_type", "MedSpanEcho").ShouldBeTrue();
    }

    [TimedFact]
    public async Task Send_InFlightCounter_IncrementsOnEntryDecrementsOnExit()
    {
        // Filter to this test's request type — see CreateStream_EarlyBreak for rationale.
        using var counterRecorder = new UpDownRecorder("warp.mediator.in_flight", "request_type", "MedSpanEcho");
        var mediator = BuildMediator(s => s.AddTransient<IRequestHandler<MedSpanEcho, string>, EchoHandler>());

        await mediator.Send(new MedSpanEcho { Value = "x" });

        counterRecorder.Sum.ShouldBe(0);
        counterRecorder.PositiveCount.ShouldBe(1);
        counterRecorder.NegativeCount.ShouldBe(1);
    }

    private sealed class MeterRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(double Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];
        private readonly Lock _lock = new();

        public MeterRecorder(string instrumentName)
        {
            _listener.InstrumentPublished = (instrument, mli) =>
            {
                if (string.Equals(instrument.Meter.Name, WarpTelemetry.ServiceName, StringComparison.Ordinal)
                    && string.Equals(instrument.Name, instrumentName, StringComparison.Ordinal))
                {
                    mli.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            {
                lock (_lock)
                {
                    _measurements.Add((value, tags.ToArray()));
                }
            });
            _listener.Start();
        }

        public bool HasMeasurementWithTagValue(string key, string value)
        {
            lock (_lock)
            {
                return _measurements.Any(m => m.Tags.Any(t => string.Equals(t.Key, key, StringComparison.Ordinal)
                    && string.Equals(t.Value?.ToString(), value, StringComparison.Ordinal)));
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class UpDownRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly string? _filterTagKey;
        private readonly string? _filterTagValue;
        private long _sum;
        private long _positive;
        private long _negative;
        private readonly Lock _lock = new();

        // `MeterListener` captures measurements process-wide. When NoDb tests run in parallel
        // and another test increments/decrements `warp.mediator.in_flight` for a different
        // request type, the unfiltered sum becomes nondeterministic. The optional tag filter
        // lets callers scope the recorder to a specific request type (e.g. "CountStream"),
        // so this test's accumulator only sees its own measurements.
        public UpDownRecorder(string instrumentName, string? filterTagKey = null, string? filterTagValue = null)
        {
            _filterTagKey = filterTagKey;
            _filterTagValue = filterTagValue;
            _listener.InstrumentPublished = (instrument, mli) =>
            {
                if (string.Equals(instrument.Meter.Name, WarpTelemetry.ServiceName, StringComparison.Ordinal)
                    && string.Equals(instrument.Name, instrumentName, StringComparison.Ordinal))
                {
                    mli.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                if (_filterTagKey != null)
                {
                    var matched = false;
                    foreach (var t in tags)
                    {
                        if (string.Equals(t.Key, _filterTagKey, StringComparison.Ordinal)
                            && string.Equals(t.Value?.ToString(), _filterTagValue, StringComparison.Ordinal))
                        {
                            matched = true;
                            break;
                        }
                    }

                    if (!matched)
                    {
                        return;
                    }
                }

                lock (_lock)
                {
                    _sum += value;
                    if (value > 0)
                    {
                        _positive += value;
                    }
                    else if (value < 0)
                    {
                        _negative += -value;
                    }
                }
            });
            _listener.Start();
        }

        public long Sum
        {
            get
            {
                lock (_lock)
                {
                    return _sum;
                }
            }
        }

        public long PositiveCount
        {
            get
            {
                lock (_lock)
                {
                    return _positive;
                }
            }
        }

        public long NegativeCount
        {
            get
            {
                lock (_lock)
                {
                    return _negative;
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
