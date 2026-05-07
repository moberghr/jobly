using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Warp.Http.Dispatch;

/// <summary>
/// <b>Generated-code support — not intended for direct use.</b> Writes an
/// <see cref="IAsyncEnumerable{T}"/> as SSE (text/event-stream) frames — one
/// <c>data: ...\n\n</c> per yielded item; honors <see cref="HttpContext.RequestAborted"/>.
/// Public only because source-generated <see cref="RequestDelegate"/> code in
/// consumer assemblies must call it.
/// </summary>
public static class SseResponseWriter
{
    public static async Task WriteAsync<T>(HttpContext context, IAsyncEnumerable<T> stream, CancellationToken cancellationToken)
    {
        var jsonOptions = context.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        // Flush headers immediately so clients can begin reading.
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var json = JsonSerializer.Serialize(item, jsonOptions.SerializerOptions);
            var frame = "data: " + json + "\n\n";
            var bytes = Encoding.UTF8.GetBytes(frame);

            await context.Response.Body.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
