using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions.Exceptions;

namespace SseAssertions.TUnit.Tests;

/// <summary>
/// End-to-end tests for the v0.3.0 refinements: <c>HasSseRetryDirectiveFirst()</c> (the
/// <c>retry:</c> directive precedes the first data event) across the string / stream / HTTP
/// receivers, and <c>EndsCleanlyOnCancellation()</c> (a cancelled read tears down via cooperative
/// cancellation, not a transport exception).
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class RetryFirstAndCleanCancellationTests
{
    private const string RetryThenData = "retry: 5000\nevent: tick\ndata: 1\n\n";
    private const string DataThenRetry = "event: tick\ndata: 1\n\nretry: 5000\n\n";
    private const string RetryOnly = "retry: 5000\n\n";
    private const string NoRetry = "event: tick\ndata: 1\n\n";

    // The standard ASP.NET Core SSE serializer writes a reconnection control frame as
    // `event: retry` + an empty `data:` line + `retry: <ms>` (the BCL fixes field order to
    // event/data/id/retry). The empty `data:` line precedes `retry:` on the wire but carries no
    // payload, so the directive is still the first event.
    private const string AspNetRetryControlFrame = "event: retry\ndata:\nretry: 5000\n\nevent: tick\ndata: 1\n\n";
    private const string RetryControlFrameOnly = "event: retry\ndata:\nretry: 5000\n\n";

    // ---- HasSseRetryDirectiveFirst ----

    [Test]
    public async Task String_RetryBeforeData_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(RetryThenData).HasSseRetryDirectiveFirst();
    }

    [Test]
    public async Task String_RetryOnlyNoData_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(RetryOnly).HasSseRetryDirectiveFirst();
    }

    [Test]
    public async Task String_CommentLineThenRetry_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A leading comment line (":...") is not a field and is skipped before the retry directive.
        await Assert.That(": keep-alive\nretry: 5000\nevent: tick\ndata: 1\n\n").HasSseRetryDirectiveFirst();
    }

    [Test]
    public async Task String_EmptyDataBeforeRetry_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A bare "data" line (no colon) is a data field with an empty value per SSE. An empty data
        // line carries no payload, so it does not count as data preceding the retry directive.
        await Assert.That("data\n\nretry: 5000\n\n").HasSseRetryDirectiveFirst();
    }

    [Test]
    public async Task String_AspNetRetryControlFrame_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // The standard ASP.NET Core control frame: `event: retry` + empty `data:` + `retry: 5000`.
        // The empty data line must not be read as data preceding the directive.
        await Assert.That(AspNetRetryControlFrame).HasSseRetryDirectiveFirst();
    }

    [Test]
    public async Task String_RetryControlFrameOnly_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(RetryControlFrameOnly).HasSseRetryDirectiveFirst();
    }

    [Test]
    public async Task String_BomBeforeRetry_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A stream that opens with a UTF-8 BOM (U+FEFF) must not hide the leading retry directive
        // from the wire-level scan; the BOM is stripped the same way SseFrameParser strips it.
        await Assert.That("\uFEFFretry: 5000\nevent: tick\ndata: 1\n\n").HasSseRetryDirectiveFirst();
    }

    [Test]
    public async Task Http_BomBeforeRetry_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse("\uFEFFretry: 5000\nevent: tick\ndata: 1\n\n", "text/event-stream");
        await Assert.That(response).HasSseRetryDirectiveFirst(cancellationToken: ct);
    }

    [Test]
    public async Task Stream_AspNetRetryControlFrame_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(AspNetRetryControlFrame);
        await Assert.That(stream).HasSseRetryDirectiveFirst(cancellationToken: ct);
    }

    [Test]
    public async Task Http_AspNetRetryControlFrame_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(AspNetRetryControlFrame, "text/event-stream");
        await Assert.That(response).HasSseRetryDirectiveFirst(cancellationToken: ct);
    }

    [Test]
    public async Task String_DataBeforeRetry_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(DataThenRetry).HasSseRetryDirectiveFirst();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("a data field appeared before the first retry directive");
    }

    [Test]
    public async Task String_NoRetry_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(NoRetry).HasSseRetryDirectiveFirst();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("no retry directive was found");
    }

    [Test]
    public async Task String_NullBody_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string nullBody = null!;
        await Assert.That(async () => await Task.Run(() => nullBody.HasSseRetryDirectiveFirst()))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Stream_RetryBeforeData_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(RetryThenData);
        await Assert.That(stream).HasSseRetryDirectiveFirst(cancellationToken: ct);
    }

    [Test]
    public async Task Stream_Null_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream nullStream = null!;
        await Assert.That(async () => await nullStream.HasSseRetryDirectiveFirst(cancellationToken: ct))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Http_RetryBeforeData_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(RetryThenData, "text/event-stream");
        await Assert.That(response).HasSseRetryDirectiveFirst(cancellationToken: ct);
    }

    [Test]
    public async Task Http_RetryFirstWithValue_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(RetryThenData, "text/event-stream");
        await Assert.That(response).HasSseRetryDirectiveFirst(5000, cancellationToken: ct);
    }

    [Test]
    public async Task Http_RetryFirstWithWrongValue_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(RetryThenData, "text/event-stream");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseRetryDirectiveFirst(9999, cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("retry: 9999");
        await Assert.That(ex.Message).Contains("retry: 5000");
    }

    [Test]
    public async Task Stream_RetryFirstWithValue_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(RetryThenData);
        await Assert.That(stream).HasSseRetryDirectiveFirst(5000, ct);
    }

    [Test]
    public async Task String_RetryFirstWithValue_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(AspNetRetryControlFrame).HasSseRetryDirectiveFirst(5000);
    }

    [Test]
    public async Task String_RetryFirstWithValue_Mismatch_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(RetryThenData).HasSseRetryDirectiveFirst(9999);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("retry: 9999");
    }

    [Test]
    public async Task String_RetryFirstWithValue_Unparseable_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That("retry: not-a-number\nevent: tick\ndata: 1\n\n").HasSseRetryDirectiveFirst(5000);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("unparseable");
    }

    [Test]
    public async Task String_RetryFirstWithValue_BareRetryNoValue_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That("retry\nevent: tick\ndata: 1\n\n").HasSseRetryDirectiveFirst(5000);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("unparseable");
    }

    [Test]
    public async Task Http_RetryFirstWithValue_WrongContentType_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(RetryThenData, "application/json");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseRetryDirectiveFirst(5000, cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("text/event-stream");
    }

    [Test]
    public async Task Http_RetryFirstWithValue_NullResponse_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HttpResponseMessage nullResponse = null!;
        await Assert.That(async () => await nullResponse.HasSseRetryDirectiveFirst(5000, cancellationToken: ct))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Stream_RetryFirstWithValue_NullStream_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream nullStream = null!;
        await Assert.That(async () => await nullStream.HasSseRetryDirectiveFirst(5000, cancellationToken: ct))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Http_NonSseStrictDefault_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(RetryThenData, "application/json");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseRetryDirectiveFirst(cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("text/event-stream");
    }

    [Test]
    public async Task Http_NullContentStrictFalse_FailsNoRetry(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseRetryDirectiveFirst(strictContentType: false, cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("no retry directive was found");
    }

    [Test]
    public async Task Http_Null_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HttpResponseMessage nullResponse = null!;
        await Assert.That(async () => await nullResponse.HasSseRetryDirectiveFirst(cancellationToken: ct))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task String_NamedRetryEventNotDirective_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A named `event: retry` with a `data:` payload is a dispatched event, not a wire-level
        // `retry:` directive. The assertion is spec-strict: it scans for the `retry:` field, finds
        // none, and the data field makes the stream fail.
        var ex = await Assert.That(async () =>
        {
            await Assert.That("event: retry\ndata: 5000\n\n").HasSseRetryDirectiveFirst();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("no retry directive was found");
    }

    [Test]
    public async Task Stream_NamedRetryEventNotDirective_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream("event: retry\ndata: 5000\n\n");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseRetryDirectiveFirst(cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("no retry directive was found");
    }

    // ---- EndsCleanlyOnCancellation ----

    [Test]
    public async Task EndsCleanly_NormalEof_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(RetryThenData);
        await Assert.That(stream).EndsCleanlyOnCancellation(ct);
    }

    [Test]
    public async Task EndsCleanly_ForeignOperationCanceled_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // An OperationCanceledException thrown while the supplied token is NOT canceled is a foreign
        // cancellation (for example, an internal read timeout), not the cooperative teardown the
        // assertion verifies, so it must fail rather than pass silently.
        using var stream = new ThrowingStream(new OperationCanceledException());
        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).EndsCleanlyOnCancellation(ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("a different token");
    }

    [Test]
    public async Task EndsCleanly_TokenCanceledBareOce_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // When the supplied token IS canceled, even a bare OperationCanceledException (one that does
        // not carry the token) is the clean cooperative-teardown signal: the discriminator is whether
        // the supplied token was canceled, so streams that throw untagged OCEs on cancellation pass.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var stream = new ThrowingStream(new OperationCanceledException());
        await Assert.That(stream).EndsCleanlyOnCancellation(cts.Token);
    }

    [Test]
    public async Task EndsCleanly_TokenDrivenCancellation_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // The read blocks until its own token is canceled, so this passes only if the assertion
        // forwards the caller's token to ReadAsync. A regression that dropped the token would leave
        // the infinite read hanging and trip the class timeout instead of passing cleanly.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var stream = new BlockUntilCanceledStream();

        await Assert.That(stream).EndsCleanlyOnCancellation(cts.Token);
    }

    [Test]
    public async Task EndsCleanly_IOException_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new ThrowingStream(new IOException("connection reset"));
        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).EndsCleanlyOnCancellation(ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("IOException");
        await Assert.That(ex.Message).Contains("end cleanly");
    }

    [Test]
    public async Task EndsCleanly_HttpRequestException_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new ThrowingStream(new HttpRequestException("transport error"));
        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).EndsCleanlyOnCancellation(ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("HttpRequestException");
    }

    [Test]
    public async Task EndsCleanly_NullStream_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream nullStream = null!;
        await Assert.That(async () => await nullStream.EndsCleanlyOnCancellation(ct))
            .Throws<ArgumentNullException>();
    }

    // ---- EndsCleanlyOnCancellation (HttpResponseMessage) ----

    [Test]
    public async Task Http_EndsCleanly_NormalEof_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(RetryThenData, "text/event-stream");
        await Assert.That(response).EndsCleanlyOnCancellation(cancellationToken: ct);
    }

    [Test]
    public async Task Http_EndsCleanly_TokenDrivenCancellation_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // The body read blocks until its own token is canceled, so this passes only if the assertion
        // forwards the caller's token to ReadAsStreamAsync / ReadAsync.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var response = BuildResponseFromStream(new BlockUntilCanceledStream(), "text/event-stream");

        await Assert.That(response).EndsCleanlyOnCancellation(cancellationToken: cts.Token);
    }

    [Test]
    public async Task Http_EndsCleanly_ForeignOperationCanceled_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponseFromStream(new ThrowingStream(new OperationCanceledException()), "text/event-stream");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).EndsCleanlyOnCancellation(cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("a different token");
    }

    [Test]
    public async Task Http_EndsCleanly_ClientTimeoutForeignToken_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // HttpClient.Timeout surfaces as a TaskCanceledException (an OperationCanceledException) whose
        // source is NOT the supplied token. A stalled server killed by the client timeout must fail
        // the assertion, not pass as a clean cooperative teardown.
        using var response = BuildResponseFromStream(new ThrowingStream(new TaskCanceledException()), "text/event-stream");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).EndsCleanlyOnCancellation(cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("a different token");
    }

    [Test]
    public async Task Http_EndsCleanly_IOException_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponseFromStream(new ThrowingStream(new IOException("connection reset")), "text/event-stream");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).EndsCleanlyOnCancellation(cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("IOException");
        await Assert.That(ex.Message).Contains("end cleanly");
    }

    [Test]
    public async Task Http_EndsCleanly_HttpRequestException_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponseFromStream(new ThrowingStream(new HttpRequestException("transport error")), "text/event-stream");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).EndsCleanlyOnCancellation(cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("HttpRequestException");
    }

    [Test]
    public async Task Http_EndsCleanly_NonSseStrictDefault_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(RetryThenData, "application/json");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).EndsCleanlyOnCancellation(cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("text/event-stream");
    }

    [Test]
    public async Task Http_EndsCleanly_NonSseStrictFalse_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(RetryThenData, "application/json");
        await Assert.That(response).EndsCleanlyOnCancellation(strictContentType: false, cancellationToken: ct);
    }

    [Test]
    public async Task Http_EndsCleanly_NullContentStrictFalse_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Force a null Content (HttpResponseMessage defaults to EmptyContent) to exercise the
        // null-content guard: with no body there is nothing to read, so teardown is trivially clean.
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = null };
        await Assert.That(response).EndsCleanlyOnCancellation(strictContentType: false, cancellationToken: ct);
    }

    [Test]
    public async Task Http_EndsCleanly_EmptyContentStrictFalse_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        await Assert.That(response).EndsCleanlyOnCancellation(strictContentType: false, cancellationToken: ct);
    }

    [Test]
    public async Task Http_EndsCleanly_Null_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HttpResponseMessage nullResponse = null!;
        await Assert.That(async () => await nullResponse.EndsCleanlyOnCancellation(cancellationToken: ct))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Http_EndsCleanly_CancellationDuringStreamAcquisition_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Cancellation that surfaces while acquiring the body stream (ReadAsStreamAsync) under the
        // supplied (canceled) token, before the read loop is entered, is still the clean cooperative
        // teardown signal the assertion checks for, not an exception that escapes it.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var response = BuildResponseFromContent(
            new ThrowingOnAcquireContent(new OperationCanceledException()), "text/event-stream");
        await Assert.That(response).EndsCleanlyOnCancellation(cancellationToken: cts.Token);
    }

    private static MemoryStream ToStream(string body) => new(Encoding.UTF8.GetBytes(body));

    private static HttpResponseMessage BuildResponseFromStream(Stream body, string contentType)
    {
        var content = new StreamContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage BuildResponse(string body, string contentType)
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage BuildResponseFromContent(HttpContent content, string contentType)
    {
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>A <see cref="Stream"/> whose read blocks until the forwarded token is canceled, for
    /// verifying that <c>EndsCleanlyOnCancellation</c> propagates the caller's token to the read.</summary>
    private sealed class BlockUntilCanceledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
            // No-op: nothing is buffered.
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A <see cref="Stream"/> whose reads always throw a configured exception, for
    /// exercising the cancellation-teardown classification in <c>EndsCleanlyOnCancellation</c>.</summary>
    private sealed class ThrowingStream(Exception toThrow) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw toThrow;

        public override int Read(byte[] buffer, int offset, int count) => throw toThrow;

        public override void Flush()
        {
            // No-op: nothing is buffered.
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>An <see cref="HttpContent"/> whose body-stream acquisition always fails with the
    /// configured exception, for exercising the cancellation-teardown classification when the
    /// failure happens in <c>ReadAsStreamAsync</c> rather than the read loop.</summary>
    private sealed class ThrowingOnAcquireContent(Exception toThrow) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromException<Stream>(toThrow);

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromException<Stream>(toThrow);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
