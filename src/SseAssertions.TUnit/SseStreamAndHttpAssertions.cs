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
/// <see cref="Stream"/> and <see cref="HttpResponseMessage"/> receivers: first-event,
/// in-order, retry-directive, content-type, and clean-cancellation checks. The frame-narrowing
/// <c>HasSseEvent</c> chain on these same receivers lives in
/// <see cref="SseStreamHasEventAssertion"/> and <see cref="SseResponseHasEventAssertion"/>; the
/// shared read and matching helpers (<see cref="SseStreamReader"/>, <see cref="SseEventMatcher"/>)
/// keep the diagnostics identical across all three receivers.
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

    /// <summary>Asserts the supplied <see cref="HttpResponseMessage"/>'s <c>Content-Type</c>
    /// header indicates a Server-Sent Events stream. Header-only check; the response body is
    /// not read. Use this as a lightweight smoke-test discriminator for SSE endpoints.</summary>
    /// <param name="response">The HTTP response whose <c>Content-Type</c> to inspect.</param>
    /// <param name="strict">When <see langword="false"/> (the default), passes when the media
    /// type is <c>text/event-stream</c> (case-insensitive); trailing parameters like
    /// <c>; charset=utf-8</c> are ignored. When <see langword="true"/>, requires the full
    /// <c>Content-Type</c> header to be exactly <c>text/event-stream</c> with no parameters.</param>
    /// <returns>An assertion that passes when the <c>Content-Type</c> matches per
    /// <paramref name="strict"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static AssertionResult HasSseContentType(
        this HttpResponseMessage response,
        bool strict = false)
    {
        ArgumentNullException.ThrowIfNull(response);

        var contentType = response.Content?.Headers?.ContentType;
        var expectedLine = strict
            ? "the response to have Content-Type exactly \"text/event-stream\""
            : "the response to have Content-Type starting with \"text/event-stream\"";

        if (contentType is null)
        {
            return AssertionResult.Failed(string.Concat(expectedLine, "\n  but got: <absent>"));
        }

        var mediaTypeMatches = string.Equals(contentType.MediaType, SseMediaType, StringComparison.OrdinalIgnoreCase);
        var passes = strict
            ? mediaTypeMatches && contentType.Parameters.Count is 0
            : mediaTypeMatches;

        return passes
            ? AssertionResult.Passed
            : AssertionResult.Failed(string.Concat(
                expectedLine,
                "\n  but got: ",
                contentType.ToString()));
    }

    /// <summary>Asserts the first SSE frame in <paramref name="response"/>'s body has
    /// <c>event:</c> equal to <paramref name="eventName"/>. Unlabelled frames match
    /// <c>HasFirstSseEvent("message")</c> per the WHATWG default-event-name rule. Body is read
    /// in full before parsing; <paramref name="cancellationToken"/> bounds the read.</summary>
    /// <param name="response">The HTTP response carrying the SSE body.</param>
    /// <param name="eventName">The event-type name expected on the first frame.</param>
    /// <param name="strictContentType">When <see langword="true"/> (the default), the assertion
    /// fails if <c>Content-Type</c>'s media type is not <c>text/event-stream</c>. Set to
    /// <see langword="false"/> for test mocks that serve SSE without the canonical header.</param>
    /// <param name="cancellationToken">Flows to the response-body read.</param>
    /// <returns>A passing assertion when the first parsed frame matches; otherwise a failing
    /// assertion describing the first event observed, "no events", the unexpected
    /// content-type diagnostic (when <paramref name="strictContentType"/> is on), or the
    /// cancellation diagnostic if the read was cut before any frame completed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> or
    /// <paramref name="eventName"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasFirstSseEvent(
        this HttpResponseMessage response,
        string eventName,
        bool strictContentType = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(eventName);

        if (strictContentType)
        {
            var mediaType = response.Content?.Headers?.ContentType?.MediaType;
            if (!string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
            }
        }

        if (response.Content is null)
        {
            return AssertionResult.Failed(string.Concat(
                "the first event to be \"",
                eventName,
                "\"\n  but the stream contained no events"));
        }

        var encoding = SseStreamReader.ResolveEncoding(response);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (body, bytesReceived, cancelled) = await SseStreamReader.ReadAsync(
            stream, encoding, cancellationToken).ConfigureAwait(false);
        return EvaluateFirstEventWithCancellation(body, bytesReceived, cancelled, eventName);
    }

    /// <summary>Asserts the first SSE frame in <paramref name="stream"/> has <c>event:</c>
    /// equal to <paramref name="eventName"/>. Unlabelled frames match
    /// <c>HasFirstSseEvent("message")</c> per the WHATWG default-event-name rule. The full
    /// stream is read before parsing; <paramref name="cancellationToken"/> bounds the read.</summary>
    /// <param name="stream">The SSE stream.</param>
    /// <param name="eventName">The event-type name expected on the first frame.</param>
    /// <param name="cancellationToken">Flows to the stream read.</param>
    /// <returns>A passing assertion when the first parsed frame matches; otherwise a failing
    /// assertion describing the first event observed, "no events", or the cancellation
    /// diagnostic if the read was cut before any frame completed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or
    /// <paramref name="eventName"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasFirstSseEvent(
        this Stream stream, string eventName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(eventName);

        var (body, bytesReceived, cancelled) = await SseStreamReader.ReadAsync(
            stream, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return EvaluateFirstEventWithCancellation(body, bytesReceived, cancelled, eventName);
    }

    /// <summary>Asserts the supplied <see cref="Stream"/> contains the named SSE events in order.
    /// When <paramref name="strictOrdering"/> is <see langword="false"/> (default), other events
    /// may appear between the named ones; when <see langword="true"/>, the named events must
    /// appear contiguously.</summary>
    /// <param name="stream">The SSE stream.</param>
    /// <param name="eventNames">The event-type names expected in order. An empty array
    /// trivially passes.</param>
    /// <param name="strictOrdering">Pass <see langword="true"/> to require the named events to
    /// appear contiguously with no other events between them.</param>
    /// <param name="cancellationToken">Flows to the stream read.</param>
    /// <returns>An assertion that passes when the order constraint is satisfied; otherwise a
    /// failing assertion describing the first violation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or
    /// <paramref name="eventNames"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseEventsInOrder(
        this Stream stream,
        System.Collections.Generic.IReadOnlyList<string> eventNames,
        bool strictOrdering = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(eventNames);

        var (body, _, _) = await SseStreamReader.ReadAsync(
            stream, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        var events = SseFrameParser.Parse(body);
        return SseEventsInOrderAssertion.Evaluate(events, eventNames, strictOrdering);
    }

    /// <summary>Asserts the supplied <see cref="HttpResponseMessage"/> body contains the named
    /// SSE events in order. When <paramref name="strictOrdering"/> is <see langword="false"/>
    /// (default), other events may appear between the named ones; when <see langword="true"/>,
    /// the named events must appear contiguously.</summary>
    /// <param name="response">The HTTP response carrying the SSE body.</param>
    /// <param name="eventNames">The event-type names expected in order. An empty array
    /// trivially passes.</param>
    /// <param name="strictOrdering">Pass <see langword="true"/> to require the named events to
    /// appear contiguously with no other events between them.</param>
    /// <param name="strictContentType">When <see langword="true"/> (the default), the assertion
    /// fails if <c>Content-Type</c>'s media type is not <c>text/event-stream</c>.</param>
    /// <param name="cancellationToken">Flows to the response-body read.</param>
    /// <returns>An assertion that passes when the order constraint is satisfied; otherwise a
    /// failing assertion describing the violation or the unexpected content-type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> or
    /// <paramref name="eventNames"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseEventsInOrder(
        this HttpResponseMessage response,
        System.Collections.Generic.IReadOnlyList<string> eventNames,
        bool strictOrdering = false,
        bool strictContentType = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(eventNames);

        if (strictContentType)
        {
            var mediaType = response.Content?.Headers?.ContentType?.MediaType;
            if (!string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
            }
        }

        if (response.Content is null)
        {
            return SseEventsInOrderAssertion.Evaluate(System.Array.Empty<SseEvent>(), eventNames, strictOrdering);
        }

        var encoding = SseStreamReader.ResolveEncoding(response);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (body, _, _) = await SseStreamReader.ReadAsync(
            stream, encoding, cancellationToken).ConfigureAwait(false);
        var events = SseFrameParser.Parse(body);
        return SseEventsInOrderAssertion.Evaluate(events, eventNames, strictOrdering);
    }

    /// <summary>Asserts the supplied <see cref="Stream"/> contains a <c>retry:</c> directive.
    /// When <paramref name="millis"/> is supplied, requires at least one frame whose
    /// <c>retry:</c> value equals it; when <see langword="null"/>, any retry value passes.</summary>
    /// <param name="stream">The SSE stream.</param>
    /// <param name="millis">The required <c>retry:</c> value in milliseconds, or
    /// <see langword="null"/> to accept any value.</param>
    /// <param name="cancellationToken">Flows to the stream read.</param>
    /// <returns>An assertion that passes when a matching <c>retry:</c> directive is found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseRetryDirective(
        this Stream stream, int? millis = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var (body, _, _) = await SseStreamReader.ReadAsync(
            stream, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        var events = SseFrameParser.Parse(body);
        return SseFormatAssertions.EvaluateRetryDirective(events, millis);
    }

    /// <summary>Asserts the supplied <see cref="HttpResponseMessage"/> body contains a
    /// <c>retry:</c> directive. When <paramref name="millis"/> is supplied, requires at least one
    /// frame whose <c>retry:</c> value equals it; when <see langword="null"/>, any retry value
    /// passes.</summary>
    /// <param name="response">The HTTP response carrying the SSE body.</param>
    /// <param name="millis">The required <c>retry:</c> value in milliseconds, or
    /// <see langword="null"/> to accept any value.</param>
    /// <param name="strictContentType">When <see langword="true"/> (the default), the assertion
    /// fails if <c>Content-Type</c>'s media type is not <c>text/event-stream</c>.</param>
    /// <param name="cancellationToken">Flows to the response-body read.</param>
    /// <returns>An assertion that passes when a matching <c>retry:</c> directive is found, or
    /// fails with the unexpected-content-type diagnostic when <paramref name="strictContentType"/>
    /// is on and the header is wrong.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseRetryDirective(
        this HttpResponseMessage response,
        int? millis = null,
        bool strictContentType = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (strictContentType)
        {
            var mediaType = response.Content?.Headers?.ContentType?.MediaType;
            if (!string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
            }
        }

        if (response.Content is null)
        {
            return SseFormatAssertions.EvaluateRetryDirective(System.Array.Empty<SseEvent>(), millis);
        }

        var encoding = SseStreamReader.ResolveEncoding(response);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (body, _, _) = await SseStreamReader.ReadAsync(
            stream, encoding, cancellationToken).ConfigureAwait(false);
        var events = SseFrameParser.Parse(body);
        return SseFormatAssertions.EvaluateRetryDirective(events, millis);
    }

    /// <summary>Asserts the supplied <see cref="Stream"/> sends a <c>retry:</c> directive before
    /// any data-bearing event: the server set the reconnection time before streaming any payload.</summary>
    /// <param name="stream">The SSE stream.</param>
    /// <param name="cancellationToken">Flows to the stream read.</param>
    /// <returns>An assertion that passes when a <c>retry:</c> directive precedes the first data event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseRetryDirectiveFirst(
        this Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var (body, _, _) = await SseStreamReader.ReadAsync(
            stream, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return SseFormatAssertions.EvaluateRetryDirectiveFirst(body);
    }

    /// <summary>Asserts the supplied <see cref="HttpResponseMessage"/> body sends a <c>retry:</c>
    /// directive before any data-bearing event.</summary>
    /// <param name="response">The HTTP response carrying the SSE body.</param>
    /// <param name="strictContentType">When <see langword="true"/> (the default), the assertion
    /// fails if <c>Content-Type</c>'s media type is not <c>text/event-stream</c>.</param>
    /// <param name="cancellationToken">Flows to the response-body read.</param>
    /// <returns>An assertion that passes when a <c>retry:</c> directive precedes the first data
    /// event, or fails with the unexpected-content-type diagnostic when
    /// <paramref name="strictContentType"/> is on and the header is wrong.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseRetryDirectiveFirst(
        this HttpResponseMessage response,
        bool strictContentType = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (strictContentType)
        {
            var mediaType = response.Content?.Headers?.ContentType?.MediaType;
            if (!string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
            }
        }

        if (response.Content is null)
        {
            return SseFormatAssertions.EvaluateRetryDirectiveFirst(string.Empty);
        }

        var encoding = SseStreamReader.ResolveEncoding(response);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (body, _, _) = await SseStreamReader.ReadAsync(
            stream, encoding, cancellationToken).ConfigureAwait(false);
        return SseFormatAssertions.EvaluateRetryDirectiveFirst(body);
    }

    /// <summary>Asserts the supplied <see cref="Stream"/> sends a <c>retry:</c> directive before any
    /// data-bearing event <em>and</em> that the leading directive's value equals
    /// <paramref name="millis"/>: position and value pinned in a single read.</summary>
    /// <param name="stream">The SSE stream.</param>
    /// <param name="millis">The required <c>retry:</c> value, in milliseconds, of the leading directive.</param>
    /// <param name="cancellationToken">Flows to the stream read.</param>
    /// <returns>An assertion that passes when a <c>retry: millis</c> directive precedes the first data event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseRetryDirectiveFirst(
        this Stream stream, int millis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var (body, _, _) = await SseStreamReader.ReadAsync(
            stream, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return SseFormatAssertions.EvaluateRetryDirectiveFirst(body, millis);
    }

    /// <summary>Asserts the supplied <see cref="HttpResponseMessage"/> body sends a <c>retry:</c>
    /// directive before any data-bearing event <em>and</em> that the leading directive's value equals
    /// <paramref name="millis"/>: position and value pinned in a single read of the forward-only
    /// response body, which a separate position assertion and value assertion cannot both inspect.</summary>
    /// <param name="response">The HTTP response carrying the SSE body.</param>
    /// <param name="millis">The required <c>retry:</c> value, in milliseconds, of the leading directive.</param>
    /// <param name="strictContentType">When <see langword="true"/> (the default), the assertion
    /// fails if <c>Content-Type</c>'s media type is not <c>text/event-stream</c>.</param>
    /// <param name="cancellationToken">Flows to the response-body read.</param>
    /// <returns>An assertion that passes when a <c>retry: millis</c> directive precedes the first data
    /// event, or fails with the unexpected-content-type diagnostic when <paramref name="strictContentType"/>
    /// is on and the header is wrong.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> HasSseRetryDirectiveFirst(
        this HttpResponseMessage response,
        int millis,
        bool strictContentType = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (strictContentType)
        {
            var mediaType = response.Content?.Headers?.ContentType?.MediaType;
            if (!string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
            }
        }

        if (response.Content is null)
        {
            return SseFormatAssertions.EvaluateRetryDirectiveFirst(string.Empty, millis);
        }

        var encoding = SseStreamReader.ResolveEncoding(response);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (body, _, _) = await SseStreamReader.ReadAsync(
            stream, encoding, cancellationToken).ConfigureAwait(false);
        return SseFormatAssertions.EvaluateRetryDirectiveFirst(body, millis);
    }

    /// <summary>Asserts the supplied <see cref="Stream"/> tears down cleanly when its read is
    /// cancelled: the read either completes normally or surfaces cooperative cancellation
    /// (<see cref="OperationCanceledException"/>), but not a transport exception
    /// (<see cref="IOException"/>, <see cref="HttpRequestException"/>). Pass a token that fires
    /// mid-stream; the assertion drains and discards content, checking only teardown behaviour.</summary>
    /// <param name="stream">The SSE stream whose cancellation teardown to verify.</param>
    /// <param name="cancellationToken">The token expected to cancel the read mid-stream.</param>
    /// <returns>A passing assertion when the read ends cleanly; otherwise a failing assertion
    /// naming the transport exception that surfaced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> EndsCleanlyOnCancellation(
        this Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return await DrainExpectingCleanCancellationAsync(
            _ => new ValueTask<Stream>(stream), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asserts the supplied <see cref="HttpResponseMessage"/> body tears down cleanly when
    /// its read is cancelled: the read either completes normally or surfaces cooperative
    /// cancellation (<see cref="OperationCanceledException"/>), but not a transport exception
    /// (<see cref="IOException"/>, <see cref="HttpRequestException"/>). Pass a token that fires
    /// mid-stream; the assertion drains and discards content, checking only teardown behaviour.</summary>
    /// <param name="response">The HTTP response carrying the SSE body whose cancellation teardown
    /// to verify.</param>
    /// <param name="strictContentType">When <see langword="true"/> (the default), the assertion
    /// fails if <c>Content-Type</c>'s media type is not <c>text/event-stream</c>. Set to
    /// <see langword="false"/> for test mocks that serve SSE without the canonical header.</param>
    /// <param name="cancellationToken">The token expected to cancel the read mid-stream.</param>
    /// <returns>A passing assertion when the read ends cleanly; otherwise a failing assertion
    /// naming the transport exception that surfaced, or the unexpected-content-type diagnostic
    /// when <paramref name="strictContentType"/> is on and the header is wrong.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static async Task<AssertionResult> EndsCleanlyOnCancellation(
        this HttpResponseMessage response,
        bool strictContentType = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var content = response.Content;

        if (strictContentType)
        {
            var mediaType = content?.Headers?.ContentType?.MediaType;
            if (!string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
            }
        }

        if (content is null)
        {
            return AssertionResult.Passed;
        }

        // Acquire the body stream inside the drain's cancellation classification: a token that
        // fires during ReadAsStreamAsync must be the same clean-teardown signal as one that fires
        // during the read loop, not an exception that escapes the assertion.
        return await DrainExpectingCleanCancellationAsync(
            ct => new ValueTask<Stream>(content.ReadAsStreamAsync(ct)),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AssertionResult> DrainExpectingCleanCancellationAsync(
        Func<CancellationToken, ValueTask<Stream>> streamFactory, CancellationToken cancellationToken)
    {
        const int BufferSize = 4096;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            try
            {
                var stream = await streamFactory(cancellationToken).ConfigureAwait(false);
                int read;
                do
                {
                    read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                }
                while (read > 0);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cooperative cancellation via the supplied token at any point in the read pipeline
                // (acquiring the body stream or reading it) is the clean teardown signal.
            }

            return AssertionResult.Passed;
        }
        catch (OperationCanceledException ex)
        {
            // An OperationCanceledException whose source is not the supplied token (most commonly a
            // TaskCanceledException from HttpClient.Timeout on a stalled server) is not the
            // cooperative teardown this assertion verifies, so it fails rather than passing silently.
            return AssertionResult.Failed(ForeignCancellationMessage(ex));
        }
        catch (IOException ex)
        {
            return AssertionResult.Failed(SseFailureMessage.UncleanCancellation(ex));
        }
        catch (HttpRequestException ex)
        {
            return AssertionResult.Failed(SseFailureMessage.UncleanCancellation(ex));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Builds the failure message for a read cancelled by a token other than the one the
    /// assertion was given (for example, an <see cref="HttpClient.Timeout"/> firing on a stalled
    /// server), which is not the cooperative teardown <c>EndsCleanlyOnCancellation</c> verifies.</summary>
    /// <param name="exception">The cancellation exception that surfaced from a foreign token.</param>
    /// <returns>The failure message naming the exception type and message.</returns>
    private static string ForeignCancellationMessage(OperationCanceledException exception)
        => string.Concat(
            "the read to be cancelled by the supplied cancellation token\n  but it was cancelled by a different token (for example, an HttpClient timeout): ",
            exception.GetType().Name,
            ": ",
            exception.Message);

    private static AssertionResult EvaluateFirstEventWithCancellation(
        string body, int bytesReceived, bool cancelled, string expectedEventName)
    {
        var events = SseFrameParser.Parse(body);
        if (events.Count is 0 && cancelled)
        {
            return AssertionResult.Failed(SseFailureMessage.CancellationCutRead(bytesReceived, 0, body));
        }

        return SseFormatAssertions.EvaluateFirstEvent(events, expectedEventName);
    }

}
