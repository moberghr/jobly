using System.Diagnostics;
using Shouldly;
using Warp.Core.Logging;
using Warp.Tests.Helpers;

namespace Warp.Tests.Observability;

/// <summary>
/// Pure unit tests for WarpTelemetry — no database. Lives in the "Telemetry" collection so
/// it serializes against other Observability NoDb tests; the harness's <see cref="ActivityListenerHarness"/>
/// is process-global and would otherwise capture spans from concurrently running test classes.
/// </summary>
[Trait("Category", "NoDb")]
[Collection("Telemetry")]
public class WarpTelemetryTests
{
    [TimedFact]
    public void GetShortTypeName_AssemblyQualifiedName_ReturnsNameWithoutAssembly()
    {
        var result = WarpTelemetry.GetShortTypeName("MyApp.Handlers.SendReport, MyApp, Version=1.0.0.0");

        result.ShouldBe("MyApp.Handlers.SendReport");
    }

    [TimedFact]
    public void GetShortTypeName_PlainTypeName_ReturnsAsIs()
    {
        var result = WarpTelemetry.GetShortTypeName("MyApp.Handlers.SendReport");

        result.ShouldBe("MyApp.Handlers.SendReport");
    }

    [TimedFact]
    public void GetShortTypeName_Null_ReturnsUnknown()
    {
        var result = WarpTelemetry.GetShortTypeName(null);

        result.ShouldBe("unknown");
    }

    [TimedFact]
    public void GetShortTypeName_EmptyString_ReturnsEmpty()
    {
        var result = WarpTelemetry.GetShortTypeName(string.Empty);

        result.ShouldBe(string.Empty);
    }

    [TimedFact]
    public void StartJobActivity_NoListener_ReturnsNull()
    {
        var traceId = Guid.NewGuid();
        var parentSpanId = ActivitySpanId.CreateRandom().ToHexString();

        var activity = WarpTelemetry.StartJobActivity(traceId, parentSpanId);

        activity.ShouldBeNull();
    }

    [TimedFact]
    public void StartJobActivity_ValidParentSpanId_SetsParentId()
    {
        using var harness = new ActivityListenerHarness();
        var traceId = Guid.NewGuid();
        var parentSpanId = ActivitySpanId.CreateRandom().ToHexString();

        var activity = WarpTelemetry.StartJobActivity(traceId, parentSpanId);

        activity.ShouldNotBeNull();
        activity.ParentSpanId.ToHexString().ShouldBe(parentSpanId);
        activity.Stop();
        activity.Dispose();
    }

    [TimedFact]
    public void StartJobActivity_MalformedParentSpanId_DoesNotThrow()
    {
        using var harness = new ActivityListenerHarness();
        var traceId = Guid.NewGuid();

        var activity = WarpTelemetry.StartJobActivity(traceId, "not-a-valid-hex");

        activity.ShouldNotBeNull();
        activity.ParentSpanId.ToHexString().ShouldBe("0000000000000000");
        activity.Stop();
        activity.Dispose();
    }

    [TimedFact]
    public void StartJobActivity_TooShortParentSpanId_DoesNotThrow()
    {
        using var harness = new ActivityListenerHarness();
        var traceId = Guid.NewGuid();

        var activity = WarpTelemetry.StartJobActivity(traceId, "abc");

        activity.ShouldNotBeNull();
        activity.ParentSpanId.ToHexString().ShouldBe("0000000000000000");
        activity.Stop();
        activity.Dispose();
    }

    [TimedFact]
    public void StartJobActivity_NullParentSpanId_SetsDefaultParentId()
    {
        using var harness = new ActivityListenerHarness();
        var traceId = Guid.NewGuid();

        var activity = WarpTelemetry.StartJobActivity(traceId, null);

        activity.ShouldNotBeNull();
        activity.ParentSpanId.ToHexString().ShouldBe("0000000000000000");
        activity.Stop();
        activity.Dispose();
    }

    [TimedFact]
    public void StartJobActivity_WithQueue_UsesProcessQueueAsSpanName()
    {
        using var harness = new ActivityListenerHarness();

        var traceId = Guid.NewGuid();
        var activity = WarpTelemetry.StartJobActivity(traceId, null, "critical");
        activity.ShouldNotBeNull();
        activity.Stop();
        activity.Dispose();

        var captured = harness.FirstByName("process critical");
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(ActivityKind.Consumer);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingSystem).ShouldBe(WarpTelemetryAttributes.MessagingSystemValue);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingOperationName).ShouldBe(WarpTelemetryAttributes.OperationProcess);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingOperationType).ShouldBe(WarpTelemetryAttributes.OperationProcess);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingDestinationName).ShouldBe("critical");
    }

    [TimedFact]
    public void StartProducerActivity_NoListener_ReturnsNull()
    {
        var activity = WarpTelemetry.StartProducerActivity("default", WarpTelemetryAttributes.OperationSend);

        activity.ShouldBeNull();
    }

    [TimedFact]
    public void StartProducerActivity_WithListener_ReturnsProducerSpanWithMessagingTags()
    {
        using var harness = new ActivityListenerHarness();

        using (var activity = WarpTelemetry.StartProducerActivity("default", WarpTelemetryAttributes.OperationSend))
        {
            activity.ShouldNotBeNull();
        }

        var captured = harness.FirstByName("send default");
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(ActivityKind.Producer);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingSystem).ShouldBe(WarpTelemetryAttributes.MessagingSystemValue);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingOperationName).ShouldBe(WarpTelemetryAttributes.OperationSend);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingOperationType).ShouldBe(WarpTelemetryAttributes.OperationSend);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingDestinationName).ShouldBe("default");
    }

    [TimedFact]
    public void StartReceiveActivity_NoListener_ReturnsNull()
    {
        var activity = WarpTelemetry.StartReceiveActivity("default");

        activity.ShouldBeNull();
    }

    [TimedFact]
    public void StartReceiveActivity_WithListener_ReturnsClientSpanWithMessagingTags()
    {
        using var harness = new ActivityListenerHarness();

        using (var activity = WarpTelemetry.StartReceiveActivity("critical"))
        {
            activity.ShouldNotBeNull();
        }

        var captured = harness.FirstByName("receive critical");
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(ActivityKind.Client);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingOperationName).ShouldBe(WarpTelemetryAttributes.OperationReceive);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingOperationType).ShouldBe(WarpTelemetryAttributes.OperationReceive);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingDestinationName).ShouldBe("critical");
    }

    [TimedFact]
    public void StartMediatorActivity_NoListener_ReturnsNull()
    {
        var activity = WarpTelemetry.StartMediatorActivity("Foo", "Bar", WarpTelemetryAttributes.MediatorKindRequest);

        activity.ShouldBeNull();
    }

    [TimedFact]
    public void StartMediatorActivity_WithListener_ReturnsInternalSpanWithMediatorTags()
    {
        using var harness = new ActivityListenerHarness();

        using (var activity = WarpTelemetry.StartMediatorActivity("GetUser", "UserDto", WarpTelemetryAttributes.MediatorKindRequest))
        {
            activity.ShouldNotBeNull();
        }

        var captured = harness.FirstByName("process GetUser");
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(ActivityKind.Internal);
        captured.GetTagItem(WarpTelemetryAttributes.MessagingDestinationName).ShouldBe("GetUser");
        captured.GetTagItem(WarpTelemetryAttributes.WarpMediatorKind).ShouldBe(WarpTelemetryAttributes.MediatorKindRequest);
        captured.GetTagItem(WarpTelemetryAttributes.WarpMediatorResponseType).ShouldBe("UserDto");
    }

    [TimedFact]
    public void StartServerTaskActivity_NoListener_ReturnsNull()
    {
        var activity = WarpTelemetry.StartServerTaskActivity("Heartbeat");

        activity.ShouldBeNull();
    }

    [TimedFact]
    public void StartServerTaskActivity_WithListener_ReturnsInternalSpanWithTaskName()
    {
        using var harness = new ActivityListenerHarness();

        using (var activity = WarpTelemetry.StartServerTaskActivity("Heartbeat"))
        {
            activity.ShouldNotBeNull();
        }

        var captured = harness.FirstByName("warp.server_task Heartbeat");
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(ActivityKind.Internal);
        captured.GetTagItem(WarpTelemetryAttributes.WarpTaskName).ShouldBe("Heartbeat");
    }

    [TimedFact]
    public void StartConcurrencyActivity_NoListener_ReturnsNull()
    {
        var activity = WarpTelemetry.StartConcurrencyActivity();

        activity.ShouldBeNull();
    }

    [TimedFact]
    public void StartConcurrencyActivity_WithListener_ReturnsInternalSpan()
    {
        using var harness = new ActivityListenerHarness();

        using (var activity = WarpTelemetry.StartConcurrencyActivity())
        {
            activity.ShouldNotBeNull();
        }

        var captured = harness.FirstByName("warp.concurrency_acquire");
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(ActivityKind.Internal);
    }

    [TimedFact]
    public void RetryMetadataKeys_MatchIRetryMetadataPropertyNames()
    {
        // Workers read retry attempt info via these literal metadata-dict keys to avoid taking
        // a project-level dependency on Warp.Core.Retry. This guard pins the literals to the
        // actual property names — a rename of either side breaks loudly here.
        WarpTelemetryAttributes.RetryMetadataRetriedTimesKey.ShouldBe(nameof(Warp.Core.Retry.IRetryMetadata.RetriedTimes));
        WarpTelemetryAttributes.RetryMetadataMaxRetriesKey.ShouldBe(nameof(Warp.Core.Retry.IRetryMetadata.MaxRetries));
    }

    [TimedFact]
    public void RetryMetadataKeys_MatchSerializedDictionaryKeys()
    {
        // Write-side guard: drive the typed IRetryMetadata setter and confirm the dict key the
        // worker reads. Catches a future regression where the metadata-impl generator changes
        // its key format (e.g. lowercases, JsonPropertyName-attributes the property) without
        // touching the property name itself — the nameof() guard above would still pass but
        // the worker's tag would silently disappear.
        var dict = new Dictionary<string, object>();
        var meta = Warp.Core.Handlers.MetadataFactory.Create<Warp.Core.Retry.IRetryMetadata>(dict);
        meta.RetriedTimes = 7;
        meta.MaxRetries = 9;

        var written = (Dictionary<string, object>)(object)meta;
        written.ContainsKey(WarpTelemetryAttributes.RetryMetadataRetriedTimesKey).ShouldBeTrue();
        written.ContainsKey(WarpTelemetryAttributes.RetryMetadataMaxRetriesKey).ShouldBeTrue();
        written[WarpTelemetryAttributes.RetryMetadataRetriedTimesKey].ShouldBe(7);
        written[WarpTelemetryAttributes.RetryMetadataMaxRetriesKey].ShouldBe(9);
    }
}
