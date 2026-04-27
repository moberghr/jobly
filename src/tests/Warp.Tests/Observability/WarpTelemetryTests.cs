using System.Diagnostics;
using Shouldly;
using Warp.Core.Logging;

namespace Warp.Tests.Observability;

/// <summary>
/// Pure unit tests for WarpTelemetry — no database, no collection fixture.
/// </summary>
[Trait("Category", "NoDb")]
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
    public void StartJobActivity_ValidParentSpanId_SetsParentId()
    {
        var traceId = Guid.NewGuid();
        var parentSpanId = ActivitySpanId.CreateRandom().ToHexString();

        var activity = WarpTelemetry.StartJobActivity(traceId, parentSpanId);

        activity.ParentSpanId.ToHexString().ShouldBe(parentSpanId);
        activity.Stop();
        activity.Dispose();
    }

    [TimedFact]
    public void StartJobActivity_MalformedParentSpanId_DoesNotThrow()
    {
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
        var traceId = Guid.NewGuid();

        var activity = WarpTelemetry.StartJobActivity(traceId, null);

        activity.ShouldNotBeNull();
        activity.ParentSpanId.ToHexString().ShouldBe("0000000000000000");
        activity.Stop();
        activity.Dispose();
    }
}
