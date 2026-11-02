using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SseAssertions;

namespace SseAssertions.Tests;

/// <summary>
/// Tests for <see cref="SseStream"/>: the connect step (returns once the first frame has arrived), the checks it
/// makes on the response, event reads that continue exactly where the previous one stopped, raw reads, and
/// disposal. Every response comes from a stub handler over a scripted body: no network, no real time.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseStreamTests
{
    private static readonly Uri Events = new("/events", UriKind.Relative);

    // --- connect step ---

    /// <summary>OpenAsync completes only once the first frame has arrived, and exposes that frame, a bare
    /// <c>retry:</c> included.</summary>
    [Test]
    public async Task OpenAsync_CompletesOnceTheFirstFrameArrives(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out var handler);

        var opening = OpenWhenReadyAsync();
        body.Send("retry: 5000\n");
        await Assert.That(opening.IsCompleted).IsFalse();

        body.Send("\n");
        await using var sse = await opening;

        await Assert.That(sse.FirstFrame.RetryMillis).IsEqualTo(5000);
        await Assert.That(sse.FirstFrame.Data).IsEmpty();
        await Assert.That(sse.Response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(handler.Accept).IsEqualTo("text/event-stream");
        await Assert.That(handler.Method).IsEqualTo(HttpMethod.Get);

        async Task<SseStream> OpenWhenReadyAsync() => await SseStream.OpenAsync(client, Events, cancellationToken);
    }

    /// <summary>A first frame that carries data is exposed with its data and is not repeated by later
    /// reads.</summary>
    [Test]
    public async Task OpenAsync_FirstFrameWithData_IsNotReadAgain(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out _);
        body.Send("event: hello\ndata: first\n\nevent: next\ndata: second\n\n");
        body.Complete();

        await using var sse = await SseStream.OpenAsync(client, Events, cancellationToken);

        await Assert.That(sse.FirstFrame.EventName).IsEqualTo("hello");
        await Assert.That(sse.FirstFrame.Data).IsEqualTo("first");
        var events = await ReadAllAsync(sse, cancellationToken);
        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].EventName).IsEqualTo("next");
    }

    /// <summary>A request's own Accept header is kept; other request details are sent as given.</summary>
    [Test]
    public async Task OpenAsync_Request_KeepsItsAcceptHeaderAndMethod(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out var handler);
        body.Send(": hi\n\n");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/events");
        request.Headers.Accept.ParseAdd("text/event-stream; q=0.9");

        await using var sse = await SseStream.OpenAsync(client, request, cancellationToken);

        await Assert.That(handler.Accept).IsEqualTo("text/event-stream; q=0.9");
        await Assert.That(handler.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(sse.FirstFrame.EventName).IsEqualTo("message");
    }

    /// <summary>A non-success status throws <see cref="HttpRequestException"/> and disposes the response.</summary>
    [Test]
    public async Task OpenAsync_NotSuccess_ThrowsAndDisposesTheResponse(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body, status: HttpStatusCode.NotFound), out _);

        await Assert.That(async () => await SseStream.OpenAsync(client, new Uri("/missing", UriKind.Relative), cancellationToken))
            .Throws<HttpRequestException>();
        await Assert.That(body.Disposed).IsTrue();
    }

    /// <summary>A response that is not <c>text/event-stream</c> (or has no Content-Type) throws, naming what it
    /// got.</summary>
    [Test]
    public async Task OpenAsync_NotAnEventStream_ThrowsNamingTheContentType(CancellationToken cancellationToken)
    {
        using var htmlClient = StubHandler.Client(() => StubHandler.Sse(new ScriptedStream(), "text/html"), out _);
        var html = await Assert.That(async () => await SseStream.OpenAsync(htmlClient, Events, cancellationToken))
            .Throws<InvalidOperationException>();
        await Assert.That(html!.Message).Contains("'text/html'");

        using var bareClient = StubHandler.Client(() => StubHandler.Sse(new ScriptedStream(), mediaType: null), out _);
        var bare = await Assert.That(async () => await SseStream.OpenAsync(bareClient, Events, cancellationToken))
            .Throws<InvalidOperationException>();
        await Assert.That(bare!.Message).Contains("missing");
    }

    /// <summary>A stream that ends before its first frame throws.</summary>
    [Test]
    public async Task OpenAsync_EndsBeforeTheFirstFrame_Throws(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out _);
        body.Send("retry: 5000\n");
        body.Complete();

        var exception = await Assert.That(async () => await SseStream.OpenAsync(client, Events, cancellationToken))
            .Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("ended before its first frame");
    }

    /// <summary>Canceling the wait for the first frame throws <see cref="OperationCanceledException"/>.</summary>
    [Test]
    public async Task OpenAsync_CanceledWhileWaiting_Throws(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out _);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                await cts.CancelAsync();
            },
            cancellationToken);

        await Assert.That(async () => await SseStream.OpenAsync(client, Events, cts.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary>Null arguments are rejected by every overload.</summary>
    [Test]
    public async Task OpenAsync_NullArguments_Throw(CancellationToken cancellationToken)
    {
        using var client = StubHandler.Client(() => StubHandler.Sse(new ScriptedStream()), out _);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/events");

        await Assert.That(async () => await SseStream.OpenAsync(null!, Events, cancellationToken)).Throws<ArgumentNullException>();
        await Assert.That(async () => await SseStream.OpenAsync(client, (Uri)null!, cancellationToken)).Throws<ArgumentNullException>();
        await Assert.That(async () => await SseStream.OpenAsync(null!, request, cancellationToken)).Throws<ArgumentNullException>();
        await Assert.That(async () => await SseStream.OpenAsync(client, (HttpRequestMessage)null!, cancellationToken)).Throws<ArgumentNullException>();
    }

    // --- event reads continue exactly ---

    /// <summary>Several frames arriving in one chunk are split between successive readers without losing any:
    /// each reader continues where the previous one stopped.</summary>
    [Test]
    public async Task Readers_OneAfterAnother_ContinueExactly(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out _);
        body.Send("retry: 1000\n\ndata: 1\n\ndata: 2\n\ndata: 3\n\n");
        await using var sse = await SseStream.OpenAsync(client, Events, cancellationToken);

        var first = new SseEventReader(sse);
        await Assert.That((await first.ReadAsync(cancellationToken))!.Data).IsEqualTo("1");

        var second = new SseEventReader(sse);
        await Assert.That((await second.ReadAsync(cancellationToken))!.Data).IsEqualTo("2");

        await using (var events = sse.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken))
        {
            await Assert.That(await events.MoveNextAsync()).IsTrue();
            await Assert.That(events.Current.Data).IsEqualTo("3");
        }

        body.Send("data: 4\n\n");
        body.Complete();
        var rest = await ReadAllAsync(sse, cancellationToken);
        await Assert.That(rest.Count).IsEqualTo(1);
        await Assert.That(rest[0].Data).IsEqualTo("4");
    }

    // --- raw reads ---

    /// <summary>A raw read after an event read returns exactly the bytes after the last line consumed, the
    /// line feed of a CRLF split across chunks excluded.</summary>
    [Test]
    public async Task RawRead_AfterAnEventRead_ContinuesAfterTheLastConsumedLine(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out _);
        body.Send("data: first\r\n\r");
        await using var sse = await SseStream.OpenAsync(client, Events, cancellationToken);
        body.Send("\nrest of the stream");
        body.Complete();

        var raw = await ReadRawToEndAsync(sse, cancellationToken);

        await Assert.That(raw).IsEqualTo("rest of the stream");
    }

    /// <summary>After a frame ended by a lone carriage return, a raw read keeps a first byte that is not a line
    /// feed, and skips a line feed that arrives alone.</summary>
    [Test]
    public async Task RawRead_AfterALoneCarriageReturn_HandlesTheNextByte(CancellationToken cancellationToken)
    {
        using var keep = new ScriptedStream();
        using var keepClient = StubHandler.Client(() => StubHandler.Sse(keep), out _);
        keep.Send("data: first\r\r");
        await using (var sse = await SseStream.OpenAsync(keepClient, Events, cancellationToken))
        {
            keep.Send("xyz");
            keep.Complete();
            await Assert.That(await ReadRawToEndAsync(sse, cancellationToken)).IsEqualTo("xyz");
        }

        using var skip = new ScriptedStream();
        using var skipClient = StubHandler.Client(() => StubHandler.Sse(skip), out _);
        skip.Send("data: first\r\r");
        await using (var sse = await SseStream.OpenAsync(skipClient, Events, cancellationToken))
        {
            skip.Send("\n");
            skip.Send("abc");
            skip.Complete();
            await Assert.That(await ReadRawToEndAsync(sse, cancellationToken)).IsEqualTo("abc");
        }
    }

    /// <summary>Synchronous raw reads (both overloads) serve the buffered bytes, then the response body.</summary>
    [Test]
    public async Task RawRead_Synchronous_ServesBufferedThenBody(CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes("data: first\n\nnext bytes");
        using var client = StubHandler.Client(() => StubHandler.Sse(new ChunkedStream(bytes, 4)), out _);
        await using var sse = await SseStream.OpenAsync(client, Events, cancellationToken);

        var (text, emptyRead) = ReadSynchronouslyToEnd(sse);

        await Assert.That(text).IsEqualTo("next bytes");
        await Assert.That(emptyRead).IsEqualTo(0);
    }

    /// <summary>A raw read with an empty buffer returns 0 without waiting, and the array overloads validate
    /// their arguments.</summary>
    [Test]
    public async Task RawRead_EmptyBufferAndInvalidArguments(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out _);
        body.Send(": ready\n\n");
        await using var sse = await SseStream.OpenAsync(client, Events, cancellationToken);

        await Assert.That(await sse.ReadAsync(Memory<byte>.Empty, cancellationToken)).IsEqualTo(0);
        await Assert.That(() => ReadArray(sse, null!, 0, 1)).Throws<ArgumentNullException>();
        await Assert.That(async () => await ReadArrayAsync(sse, new byte[2], 1, 5, cancellationToken)).Throws<ArgumentOutOfRangeException>();
        body.Send("abc");
        await Assert.That(await ReadArrayAsync(sse, new byte[8], 2, 3, cancellationToken)).IsEqualTo(3);
    }

    // --- stream members and disposal ---

    /// <summary>The stream is read-only and forward-only; disposing it disposes the response body.</summary>
    [Test]
    public async Task StreamMembers_AreReadOnlyForwardOnly_AndDisposeDisposesTheResponse(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out _);
        body.Send(": ready\n\n");
        var sse = await SseStream.OpenAsync(client, Events, cancellationToken);

        await Assert.That(sse.CanRead).IsTrue();
        await Assert.That(sse.CanSeek).IsFalse();
        await Assert.That(sse.CanWrite).IsFalse();
        await Assert.That(() => sse.Length).Throws<NotSupportedException>();
        await Assert.That(() => sse.Position).Throws<NotSupportedException>();
        await Assert.That(() => sse.Position = 1).Throws<NotSupportedException>();
        await Assert.That(() => sse.Seek(0, SeekOrigin.Begin)).Throws<NotSupportedException>();
        await Assert.That(() => sse.SetLength(1)).Throws<NotSupportedException>();
        await Assert.That(() => sse.Write([1], 0, 1)).Throws<NotSupportedException>();
        await sse.FlushAsync(cancellationToken);

        await sse.DisposeAsync();
        await Assert.That(body.Disposed).IsTrue();
    }

    /// <summary>Reads the rest of <paramref name="stream"/> with the synchronous overloads, alternating the
    /// array and span forms.</summary>
    private static (string Text, int EmptyRead) ReadSynchronouslyToEnd(Stream stream)
    {
        var buffer = new byte[64];
        var total = 0;
        int read;
        while ((read = total % 2 == 0 ? stream.Read(buffer, total, buffer.Length - total) : stream.Read(buffer.AsSpan(total))) > 0)
        {
            total += read;
        }

        return (Encoding.UTF8.GetString(buffer, 0, total), stream.Read(Span<byte>.Empty));
    }

    /// <summary>Calls the synchronous array overload of <see cref="Stream.Read(byte[], int, int)"/>.</summary>
    private static int ReadArray(Stream stream, byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);

    /// <summary>Calls the legacy array overload of <c>ReadAsync</c>, which this test exists to cover.</summary>
    [SuppressMessage(
        "Performance",
        "CA1835:Prefer the 'Memory'-based overloads for 'ReadAsync' and 'WriteAsync'",
        Justification = "The test covers the array-based ReadAsync override itself; calling the Memory overload would bypass it.")]
    private static Task<int> ReadArrayAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => stream.ReadAsync(buffer, offset, count, cancellationToken);

    /// <summary>Reads every remaining event of <paramref name="stream"/>.</summary>
    private static async Task<List<SseEvent>> ReadAllAsync(SseStream stream, CancellationToken cancellationToken)
    {
        var events = new List<SseEvent>();
        await foreach (var evt in stream.ReadEventsAsync(cancellationToken))
        {
            events.Add(evt);
        }

        return events;
    }

    /// <summary>Reads the rest of <paramref name="stream"/> as raw UTF-8 text.</summary>
    private static async Task<string> ReadRawToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return Encoding.UTF8.GetString(copy.ToArray());
    }
}
