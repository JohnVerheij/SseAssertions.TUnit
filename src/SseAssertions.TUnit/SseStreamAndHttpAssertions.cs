using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// Fluent TUnit entry points for asserting on Server-Sent Events streams produced by
/// <see cref="Stream"/> and <see cref="HttpResponseMessage"/> receivers. These flat-form methods
/// complement the <c>HasSseEvent</c> chain on the <see cref="string"/> receiver:
/// async receivers require the body read to happen inside the assertion call site, which is
/// awkward to compose with a synchronous chain, so the flat-form encodes the most common shape
/// (event-name + minimum count + optional content-type validation + cancellation handling) in a
/// single call.
/// </summary>
/// <remarks>
/// <para>Cancellation semantics: if the supplied <see cref="CancellationToken"/> fires during
/// the body read, the partial buffer is parsed as best-effort SSE and the assertion runs against
/// the partial event list. On chain pass, the assertion passes; on chain fail, the failure
/// message renders the cancellation-cut-the-read variant
/// (<see cref="SseFailureMessage.CancellationCutRead(int, int, string)"/>).</para>
/// <para>On <see cref="HttpResponseMessage"/>, <c>strictContentType</c> defaults to
/// <see langword="true"/>: a response without <c>Content-Type: text/event-stream</c> fails with
/// the <see cref="SseFailureMessage.UnexpectedContentType(string?)"/> diagnostic. Pass
/// <see langword="false"/> for test mocks that serve SSE without the canonical header.</para>
/// </remarks>
[SuppressMessage(
    "Usage",
    "VSTHRD200:Use \"Async\" suffix for async methods",
    Justification = "These are [GenerateAssertion] source methods: the method name becomes the fluent chain entry point (Assert.That(response).HasSseEvent(...)), so an Async suffix would corrupt the assertion surface.")]
public static class SseStreamAndHttpAssertions
{
    private const string SseMediaType = "text/event-stream";

    /// <summary>Asserts the supplied <see cref="Stream"/> contains at least
    /// <paramref name="minCount"/> SSE frames of type <paramref name="eventName"/>.</summary>
    /// <param name="stream">The SSE stream. Read to its end (or until <paramref name="cancellationToken"/>
    /// fires).</param>
    /// <param name="eventName">The SSE event-type name to look for.</param>
    /// <param name="minCount">The minimum match count. Defaults to <c>1</c>.</param>
    /// <param name="cancellationToken">Flows to the stream read.</param>
    /// <returns>An assertion that passes when the stream contains at least
    /// <paramref name="minCount"/> matching frames.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or
    /// <paramref name="eventName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minCount"/> is
    /// negative.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseEvent(
        this Stream stream, string eventName, int minCount = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentOutOfRangeException.ThrowIfNegative(minCount);

        var (body, bytesReceived, cancelled) = await ReadStreamWithCancellationCaptureAsync(
            stream, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return EvaluateAgainstParsedEvents(body, bytesReceived, cancelled, eventName, minCount);
    }

    /// <summary>Asserts the supplied <see cref="HttpResponseMessage"/> body contains at least
    /// <paramref name="minCount"/> SSE frames of type <paramref name="eventName"/>.</summary>
    /// <param name="response">The HTTP response carrying the SSE body.</param>
    /// <param name="eventName">The SSE event-type name to look for.</param>
    /// <param name="minCount">The minimum match count. Defaults to <c>1</c>.</param>
    /// <param name="strictContentType">When <see langword="true"/> (the default), the assertion
    /// fails if <see cref="HttpContent.Headers"/>'s <c>Content-Type</c> media type is not
    /// <c>text/event-stream</c>. Set to <see langword="false"/> for test mocks that serve SSE
    /// without the canonical header.</param>
    /// <param name="cancellationToken">Flows to the response-body read.</param>
    /// <returns>An assertion that passes when the response is SSE-typed (subject to
    /// <paramref name="strictContentType"/>) and contains at least <paramref name="minCount"/>
    /// matching frames.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> or
    /// <paramref name="eventName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minCount"/> is
    /// negative.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseEvent(
        this HttpResponseMessage response,
        string eventName,
        int minCount = 1,
        bool strictContentType = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentOutOfRangeException.ThrowIfNegative(minCount);

        if (strictContentType)
        {
            var mediaType = response.Content?.Headers?.ContentType?.MediaType;
            if (!string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
            }
        }

        var encoding = ResolveEncoding(response);
        var stream = await response.Content!.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (body, bytesReceived, cancelled) = await ReadStreamWithCancellationCaptureAsync(
            stream, encoding, cancellationToken).ConfigureAwait(false);
        return EvaluateAgainstParsedEvents(body, bytesReceived, cancelled, eventName, minCount);
    }

    private static AssertionResult EvaluateAgainstParsedEvents(
        string body, int bytesReceived, bool cancelled, string eventName, int minCount)
    {
        var events = SseFrameParser.Parse(body);
        var matchCount = CountMatching(events, eventName);

        if (matchCount >= minCount)
        {
            return AssertionResult.Passed;
        }

        if (cancelled)
        {
            return AssertionResult.Failed(SseFailureMessage.CancellationCutRead(bytesReceived, events.Count, body));
        }

        return AssertionResult.Failed(SseFailureMessage.EventCountMismatch(
            eventName, minCount, matchCount, SseCountComparison.AtLeast));
    }

    private static int CountMatching(System.Collections.Generic.IReadOnlyList<SseEvent> events, string eventName)
    {
        // Per-item filter against a single fixed string. Hand-written count avoids a
        // LINQ allocation that S3267 would otherwise prefer; the loop shape is unavoidable.
        var n = 0;
        for (var i = 0; i < events.Count; i++)
        {
            if (string.Equals(events[i].EventName, eventName, StringComparison.Ordinal))
            {
                n++;
            }
        }

        return n;
    }

    private static Encoding ResolveEncoding(HttpResponseMessage response)
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

    private static async Task<(string Body, int BytesReceived, bool Cancelled)> ReadStreamWithCancellationCaptureAsync(
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
}
