using System.Diagnostics;
using Jobly.Core.Logging;
using Shouldly;

namespace Jobly.Tests.Observability;

/// <summary>
/// Pure unit tests for JoblyTelemetry — no database, no collection fixture.
/// </summary>
[Trait("Category", "NoDb")]
public class JoblyTelemetryTests
{
    [TimedFact]
    public void GetShortTypeName_AssemblyQualifiedName_ReturnsNameWithoutAssembly()
    {
        var result = JoblyTelemetry.GetShortTypeName("MyApp.Handlers.SendReport, MyApp, Version=1.0.0.0");

        result.ShouldBe("MyApp.Handlers.SendReport");
    }

    [TimedFact]
    public void GetShortTypeName_PlainTypeName_ReturnsAsIs()
    {
        var result = JoblyTelemetry.GetShortTypeName("MyApp.Handlers.SendReport");

        result.ShouldBe("MyApp.Handlers.SendReport");
    }

    [TimedFact]
    public void GetShortTypeName_Null_ReturnsUnknown()
    {
        var result = JoblyTelemetry.GetShortTypeName(null);

        result.ShouldBe("unknown");
    }

    [TimedFact]
    public void GetShortTypeName_EmptyString_ReturnsEmpty()
    {
        var result = JoblyTelemetry.GetShortTypeName(string.Empty);

        result.ShouldBe(string.Empty);
    }

    [TimedFact]
    public void StartJobActivity_ValidParentSpanId_SetsParentId()
    {
        var traceId = Guid.NewGuid();
        var parentSpanId = ActivitySpanId.CreateRandom().ToHexString();

        var activity = JoblyTelemetry.StartJobActivity(traceId, parentSpanId);

        activity.ParentSpanId.ToHexString().ShouldBe(parentSpanId);
        activity.Stop();
        activity.Dispose();
    }

    [TimedFact]
    public void StartJobActivity_MalformedParentSpanId_DoesNotThrow()
    {
        var traceId = Guid.NewGuid();

        var activity = JoblyTelemetry.StartJobActivity(traceId, "not-a-valid-hex");

        activity.ShouldNotBeNull();
        activity.ParentSpanId.ToHexString().ShouldBe("0000000000000000");
        activity.Stop();
        activity.Dispose();
    }

    [TimedFact]
    public void StartJobActivity_TooShortParentSpanId_DoesNotThrow()
    {
        var traceId = Guid.NewGuid();

        var activity = JoblyTelemetry.StartJobActivity(traceId, "abc");

        activity.ShouldNotBeNull();
        activity.ParentSpanId.ToHexString().ShouldBe("0000000000000000");
        activity.Stop();
        activity.Dispose();
    }

    [TimedFact]
    public void StartJobActivity_NullParentSpanId_SetsDefaultParentId()
    {
        var traceId = Guid.NewGuid();

        var activity = JoblyTelemetry.StartJobActivity(traceId, null);

        activity.ShouldNotBeNull();
        activity.ParentSpanId.ToHexString().ShouldBe("0000000000000000");
        activity.Stop();
        activity.Dispose();
    }
}
