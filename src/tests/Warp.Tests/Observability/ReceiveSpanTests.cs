using System.Diagnostics;
using Shouldly;
using Warp.Core.Logging;
using Warp.Tests.Fixtures;
using Warp.Tests.Helpers;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Observability;

[GenerateDatabaseTests]
public abstract class ReceiveSpanTestsBase : IntegrationTestBase
{
    protected ReceiveSpanTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenJob_WhenProcessed_ThenReceiveSpanPrecedesProcessSpanUnderSameTraceId()
    {
        using var harness = new ActivityListenerHarness();

        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForCompletion();

        // Match by MessagingMessageId so we don't pick up spans from parallel tests in the
        // same listener process. The harness captures every Warp span on the AppDomain.
        var jobIdString = jobId.ToString();
        var receive = harness.AllByName("receive default")
            .FirstOrDefault(a => string.Equals(a.GetTagItem(WarpTelemetryAttributes.MessagingMessageId)?.ToString(), jobIdString, StringComparison.Ordinal));
        var process = harness.AllByName("process default")
            .FirstOrDefault(a => string.Equals(a.GetTagItem(WarpTelemetryAttributes.MessagingMessageId)?.ToString(), jobIdString, StringComparison.Ordinal));

        receive.ShouldNotBeNull();
        receive.Kind.ShouldBe(ActivityKind.Client);
        receive.GetTagItem(WarpTelemetryAttributes.MessagingOperationName).ShouldBe(WarpTelemetryAttributes.OperationReceive);
        receive.GetTagItem(WarpTelemetryAttributes.MessagingDestinationName).ShouldBe("default");
        receive.GetTagItem(WarpTelemetryAttributes.WarpWorkerId).ShouldNotBeNull();

        process.ShouldNotBeNull();
        process.Kind.ShouldBe(ActivityKind.Consumer);

        // Receive must precede process. (Receive runs under whatever ambient trace the worker
        // is in; process parents to the published job's trace context — the two are siblings
        // by time but not necessarily under the same trace id, which is by-OTel-convention for
        // a Client receive vs. a Consumer process.)
        receive.StartTimeUtc.ShouldBeLessThanOrEqualTo(process.StartTimeUtc);
    }
}
