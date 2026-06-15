using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SseAssertions.TUnit;

/// <summary>
/// Shared stream-draining helper for the SSE assertion entry points. Reads a stream to its end (or
/// until cancellation), capturing whether the supplied token cut the read short and how many bytes
/// arrived, so callers can distinguish a cancellation-truncated body from a genuine miss.
/// </summary>
internal static class SseStreamReader
{
    /// <summary>Reads <paramref name="stream"/> fully into a decoded string, capturing the byte
    /// count and whether the read was cancelled.</summary>
    /// <param name="stream">The stream to drain.</param>
    /// <param name="encoding">The encoding used to decode the accumulated bytes.</param>
    /// <param name="cancellationToken">Flows to the stream read; cancellation ends the read and is
    /// reported via the returned <c>Cancelled</c> flag rather than thrown.</param>
    /// <returns>The decoded body, the number of bytes received, and whether the read was
    /// cancelled.</returns>
    public static async Task<(string Body, int BytesReceived, bool Cancelled)> ReadAsync(
        Stream stream, Encoding encoding, CancellationToken cancellationToken)
    {
        const int InitialBufferSize = 4096;
        var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        using var ms = new MemoryStream();
        var cancelled = false;
        try
        {
            int read;
            try
            {
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var bytes = ms.ToArray();
        return (encoding.GetString(bytes), bytes.Length, cancelled);
    }

    /// <summary>Resolves the response body's text encoding from its <c>Content-Type</c> charset,
    /// falling back to UTF-8 (the WHATWG SSE default) when the charset is absent or unknown.</summary>
    /// <param name="response">The HTTP response whose charset to resolve.</param>
    /// <returns>The resolved encoding, or UTF-8 when none applies.</returns>
    public static Encoding ResolveEncoding(HttpResponseMessage response)
    {
        var charset = response.Content?.Headers?.ContentType?.CharSet;
        if (string.IsNullOrEmpty(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            // Unknown / invalid charset — fall back to UTF-8 per WHATWG SSE default.
            return Encoding.UTF8;
        }
    }
}
