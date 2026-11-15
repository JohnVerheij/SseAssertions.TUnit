using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SseAssertions.TUnit;

/// <summary>
/// Stream-draining helper for the retry-directive assertions, which inspect fields of frames that carry no event (a
/// bare <c>retry:</c>). Reads a stream to its end, or until the supplied token ends the read. The event assertions
/// read incrementally through <see cref="SseAssertions.SseEventReader"/> instead.
/// </summary>
internal static class SseStreamReader
{
    /// <summary>Reads <paramref name="stream"/> to its end, or until <paramref name="cancellationToken"/> ends the
    /// read, and decodes it as UTF-8, as the SSE specification requires.</summary>
    /// <param name="stream">The stream to drain.</param>
    /// <param name="cancellationToken">Flows to the stream read. Its cancellation ends the read without throwing,
    /// and the text then stops after the last line break: the read can stop inside a line, and a field is complete
    /// only at its line break (<c>retry: 3</c>, cut from <c>retry: 3000</c>, is no directive). A cancellation by
    /// another token propagates.</param>
    /// <returns>The decoded text.</returns>
    public static async Task<string> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        const int InitialBufferSize = 4096;
        var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        using var ms = new MemoryStream();
        var canceled = false;
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only the supplied token's cancellation ends the read quietly. A foreign cancellation (for example
                // an HttpClient timeout, whose token is not the one passed here) propagates instead of being taken
                // for the end of the body, matching the EndsCleanlyOnCancellation distinction added in 0.5.1.
                canceled = true;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        ReadOnlySpan<byte> data = ms.ToArray();
        if (canceled)
        {
            // In UTF-8, CR and LF never occur inside another character, so the cut cannot split one.
            data = data[..(data.LastIndexOfAny("\r\n"u8) + 1)];
        }

        return Encoding.UTF8.GetString(data);
    }

    /// <summary>Acquires an HTTP response's SSE body stream and drains it as <see cref="ReadAsync"/> does. The
    /// stream acquisition is intentionally not passed the token: a cancellation during acquisition would throw out
    /// of the assertion, so the token bounds only the body read, where its cancellation ends the read quietly.</summary>
    /// <param name="response">The HTTP response whose body to read. Its <c>Content</c> is never null
    /// (the getter returns an empty content), so the read is always safe.</param>
    /// <param name="cancellationToken">Bounds the body read.</param>
    /// <returns>The decoded body, up to the last line break when <paramref name="cancellationToken"/> ended the
    /// read.</returns>
    public static async Task<string> ReadResponseBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // CancellationToken.None is deliberate: cancellation during acquisition must not throw out of the assertion;
        // the token below bounds the body read.
        var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None).ConfigureAwait(false);
        return await ReadAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
