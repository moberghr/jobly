using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Warp.Http;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class StreamEndpointTests
{
    [TimedFact]
    public async Task Stream_Returns200WithEventStreamContentType()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.GetAsync(new Uri("/api/stream/numbers?Count=3", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        resp.Content.Headers.ContentType.ShouldBe(new MediaTypeHeaderValue("text/event-stream") { CharSet = "utf-8" });
    }

    [TimedFact]
    public async Task Stream_EmitsOneDataFramePerYieldedItem()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.GetAsync(new Uri("/api/stream/numbers?Count=3", UriKind.Relative));
        var body = await resp.Content.ReadAsStringAsync();

        body.ShouldContain("data: 0\n\n");
        body.ShouldContain("data: 1\n\n");
        body.ShouldContain("data: 2\n\n");
    }

    [TimedFact]
    public async Task Stream_HonorsRequestAbortedWhenClientCancels()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        // Count = 1_000_000 ensures the handler is still mid-stream when the client cancels.
        // If RequestAborted were ignored, the handler would block past the cancellation deadline
        // and the test would hit the [TimedFact] 10s ceiling.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var resp = await app.Client.GetAsync(
                new Uri("/api/stream/numbers?Count=1000000", UriKind.Relative),
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            await resp.Content.ReadAsStringAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected — cancellation propagated as cancellation.
        }
        catch (HttpRequestException)
        {
            // Also expected — when the client aborts mid-body, HttpClient surfaces the
            // truncated stream as "Error while copying content to a stream." The point
            // of the test is the timing budget, not the exception shape.
        }

        sw.Stop();

        // Cancellation must take effect well within the TimedFact deadline. A handler that
        // ignored RequestAborted would streaming-write 1M frames and never finish in 5s.
        // Loose budget tolerates cold-start overhead in TestServer; tight enough to fail
        // if RequestAborted is silently dropped.
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }
}
