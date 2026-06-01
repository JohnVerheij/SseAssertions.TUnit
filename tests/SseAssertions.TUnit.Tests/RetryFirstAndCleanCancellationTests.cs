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
    public async Task String_BareDataFieldBeforeRetry_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // "data" with no colon is a data field (empty value) per SSE; it still counts as data-first.
        var ex = await Assert.That(async () =>
        {
            await Assert.That("data\n\nretry: 5000\n\n").HasSseRetryDirectiveFirst();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("a data field appeared before the first retry directive");
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

    // ---- EndsCleanlyOnCancellation ----

    [Test]
    public async Task EndsCleanly_NormalEof_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(RetryThenData);
        await Assert.That(stream).EndsCleanlyOnCancellation(ct);
    }

    [Test]
    public async Task EndsCleanly_OperationCanceled_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new ThrowingStream(new OperationCanceledException());
        await Assert.That(stream).EndsCleanlyOnCancellation(ct);
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

    private static MemoryStream ToStream(string body) => new(Encoding.UTF8.GetBytes(body));

    private static HttpResponseMessage BuildResponse(string body, string contentType)
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
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
}
