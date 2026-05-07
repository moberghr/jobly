using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Warp.Http;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class StreamEdgeCasesTests
{
    [TimedFact]
    public async Task EmptyStream_Returns200WithNoFrames()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.GetAsync(new Uri("/api/stream/empty", UriKind.Relative));
        var body = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task SingleItemStream_EmitsOneFrame()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.GetAsync(new Uri("/api/stream/numbers?Count=1", UriKind.Relative));
        var body = await resp.Content.ReadAsStringAsync();

        body.ShouldBe("data: 0\n\n");
    }

    [TimedFact]
    public async Task LargeStream_EmitsAllFrames()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.GetAsync(new Uri("/api/stream/numbers?Count=200", UriKind.Relative));
        var body = await resp.Content.ReadAsStringAsync();

        // Each frame is "data: <n>\n\n" — count the frame separators.
        var frames = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        frames.Length.ShouldBe(200);
    }

    [TimedFact]
    public async Task StreamThrowsMidWay_FirstFrameDelivered_ExceptionPropagates()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        // First yield delivers "first", then the handler throws — we expect either an exception
        // on read or a partial body containing the first frame, depending on TestHost behavior.
        try
        {
            var resp = await app.Client.GetAsync(new Uri("/api/stream/throws", UriKind.Relative));
            var body = await resp.Content.ReadAsStringAsync();

            body.ShouldContain("data: \"first\"\n\n");
        }
        catch (HttpRequestException)
        {
            // Acceptable — TestHost surfaced the handler exception.
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("stream-failure", StringComparison.Ordinal))
        {
            // Acceptable — direct exception propagation through TestHost.
        }
    }
}
