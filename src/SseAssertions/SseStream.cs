using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SseAssertions;

/// <summary>
/// An open Server-Sent Events response stream whose first frame has arrived. <see cref="OpenAsync(HttpClient, Uri, CancellationToken)"/>
/// returns once the server has written its first frame, which an endpoint does after it has subscribed to the
/// events it streams. That is the moment a test can trigger the events and be sure none is missed.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="SseStream"/> is the response body: a read-only <see cref="Stream"/> positioned after the first frame. Every
/// assertion that reads a <see cref="Stream"/> applies to it, and each one continues where the previous one
/// stopped, so a test can assert one event, trigger the next, and assert again:
/// </para>
/// <code>
/// await using var sse = await SseStream.OpenAsync(client, new Uri("/events", UriKind.Relative), ct);
/// await service.TriggerAsync(ct);
/// await Assert.That(sse).HasSseEvent("cycleCompleted", ct);
/// </code>
/// <para>
/// Read events directly with <see cref="ReadEventsAsync"/>, or with an <see cref="SseEventReader"/>. Mixing
/// event reads with raw reads of the stream is possible, but a raw read continues after the last line an event
/// read consumed. Not thread-safe. Disposing the stream disposes the response.
/// </para>
/// </remarks>
public sealed class SseStream : Stream
{
    private const string EventStreamMediaType = "text/event-stream";

    private readonly HttpResponseMessage _response;
    private readonly Stream _body;

    private SseStream(HttpResponseMessage response, Stream body, SseStreamParser parser, SseEvent firstFrame)
    {
        _response = response;
        _body = body;
        Parser = parser;
        FirstFrame = firstFrame;
    }

    /// <summary>Gets the HTTP response. Its headers and status are available; read the body through this
    /// stream, not through the response, which no longer has the bytes already read.</summary>
    public HttpResponseMessage Response => _response;

    /// <summary>Gets the first frame the server wrote, for example its <c>retry:</c> directive. Its
    /// <see cref="SseEvent.Data"/> is empty when the frame carried no data, such as a bare <c>retry:</c> or a
    /// comment.</summary>
    public SseEvent FirstFrame { get; }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <summary>Not supported: the stream is forward-only.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override long Length => throw new NotSupportedException();

    /// <summary>Not supported: the stream is forward-only.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Gets the parser shared by every <see cref="SseEventReader"/> over this stream.</summary>
    internal SseStreamParser Parser { get; }

    /// <summary>
    /// Sends a GET request for <paramref name="requestUri"/> and returns once the first frame of the
    /// Server-Sent Events response has arrived.
    /// </summary>
    /// <param name="client">The client to send the request with, for example one created by a
    /// <c>WebApplicationFactory</c>.</param>
    /// <param name="requestUri">The endpoint, absolute or relative to the client's base address.</param>
    /// <param name="cancellationToken">Cancels the request and the wait for the first frame.</param>
    /// <returns>The open stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="requestUri"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="HttpRequestException">The response status is not a success code.</exception>
    /// <exception cref="InvalidOperationException">The response is not <c>text/event-stream</c>, or it ended
    /// before its first frame.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled
    /// first.</exception>
    public static async Task<SseStream> OpenAsync(HttpClient client, Uri requestUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        return await OpenAsync(client, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends <paramref name="request"/> and returns once the first frame of the Server-Sent Events response has
    /// arrived. Use this overload for a request that needs headers (for example <c>Last-Event-ID</c> or
    /// authorization) or another method.
    /// </summary>
    /// <param name="client">The client to send the request with.</param>
    /// <param name="request">The request. An <c>Accept: text/event-stream</c> header is added when it has no
    /// <c>Accept</c> header.</param>
    /// <param name="cancellationToken">Cancels the request and the wait for the first frame.</param>
    /// <returns>The open stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="HttpRequestException">The response status is not a success code.</exception>
    /// <exception cref="InvalidOperationException">The response is not <c>text/event-stream</c>, or it ended
    /// before its first frame.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled
    /// first.</exception>
    public static async Task<SseStream> OpenAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Headers.Accept.Count is 0)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(EventStreamMediaType));
        }

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await ReadFirstFrameAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Reads the events after the first frame as they arrive. A frame without a <c>data:</c> line is
    /// skipped, as the SSE specification requires. Stopping the enumeration leaves the stream positioned at
    /// the next event.</summary>
    /// <param name="cancellationToken">Cancels the wait for the next event.</param>
    /// <returns>The events, until the server ends the stream.</returns>
    public async IAsyncEnumerable<SseEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = new SseEventReader(this);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) is { } evt)
        {
            yield return evt;
        }
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Parser.ReadRaw(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => Parser.ReadRaw(buffer);

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Parser.ReadRawAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => Parser.ReadRawAsync(buffer, cancellationToken);

    /// <summary>Does nothing: the stream is read-only.</summary>
    public override void Flush()
    {
        // Read-only: nothing to flush.
    }

    /// <summary>Not supported: the stream is forward-only.</summary>
    /// <param name="offset">Ignored.</param>
    /// <param name="origin">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <summary>Not supported: the stream is read-only.</summary>
    /// <param name="value">Ignored.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Not supported: the stream is read-only.</summary>
    /// <param name="buffer">Ignored.</param>
    /// <param name="offset">Ignored.</param>
    /// <param name="count">Ignored.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
            _response.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Checks the response and reads its first frame.</summary>
    private static async Task<SseStream> ReadFirstFrameAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, EventStreamMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected a {EventStreamMediaType} response but the Content-Type was {(mediaType is null ? "missing" : "'" + mediaType + "'")}.");
        }

        var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var parser = new SseStreamParser(body);
        var first = await parser.ReadFrameAsync(new SseReadProgress(), cancellationToken).ConfigureAwait(false);
        if (first is not { } frame)
        {
            await body.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("The event stream ended before its first frame.");
        }

        return new SseStream(response, body, parser, frame.Event);
    }
}
