using System;
using System.Globalization;
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
/// End-to-end tests for the frame narrowers (<c>WithDataParsedAs&lt;T&gt;</c>, <c>WithData</c>,
/// <c>WithId</c>, <c>WithRetryMillis</c>) on the streaming <c>HasSseEvent</c> chains
/// (<see cref="Stream"/> and <see cref="HttpResponseMessage"/> receivers) added in 0.7.0. Verifies
/// the narrowers reach the partial-read path, produce the same per-narrower diagnostics as the
/// <see cref="string"/> chain, and that the cancellation-cut diagnostic still wins over a narrower
/// miss.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseStreamingNarrowerTests
{
    private const string TwoPrices =
        "event: price\ndata: 10\nid: a\n\nevent: price\ndata: 20\nid: b\n\n";

    private static int ParseInt(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    [Test]
    public async Task Response_WithDataParsedAs_MatchingFrame_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(TwoPrices, "text/event-stream");

        await Assert.That(response).HasSseEvent("price", cancellationToken: ct)
            .WithDataParsedAs(ParseInt, v => v > 15)
            .AtLeast(1);
    }

    [Test]
    public async Task Response_WithDataParsedAs_NoFrameMatches_FailsWithDataPredicate(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(TwoPrices, "text/event-stream");

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEvent("price", cancellationToken: ct)
                .WithDataParsedAs(ParseInt, v => v > 100)
                .AtLeast(1);
        }).Throws<AssertionException>();

        // Both observed data values are listed so the consumer can see why nothing matched.
        await Assert.That(ex!.Message).Contains("10");
        await Assert.That(ex.Message).Contains("20");
    }

    [Test]
    public async Task Response_WithDataParsedAs_ParserThrows_FailsWithDeserializationDiagnostic(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(TwoPrices, "text/event-stream");

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEvent("price", cancellationToken: ct)
                .WithDataParsedAs(s => throw new FormatException("bad " + s), (string _) => true)
                .AtLeast(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("FormatException");
    }

    [Test]
    public async Task Response_WithId_MatchingFrame_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(TwoPrices, "text/event-stream");

        await Assert.That(response).HasSseEvent("price", cancellationToken: ct)
            .WithId("b")
            .Exactly(1);
    }

    [Test]
    public async Task Response_WithId_NoMatch_FailsWithIdDiagnostic(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(TwoPrices, "text/event-stream");

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEvent("price", cancellationToken: ct)
                .WithId("z")
                .AtLeast(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("z");
    }

    [Test]
    public async Task Response_WithData_AndAtMost_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(TwoPrices, "text/event-stream");

        // Exactly one "price" frame has data "10"; AtMost(1) is satisfied.
        await Assert.That(response).HasSseEvent("price", cancellationToken: ct)
            .WithData(d => string.Equals(d, "10", StringComparison.Ordinal))
            .AtMost(1);
    }

    [Test]
    public async Task Response_WithRetryMillis_MatchingFrame_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: ping\ndata: x\nretry: 5000\n\n";
        using var response = BuildResponse(body, "text/event-stream");

        await Assert.That(response).HasSseEvent("ping", cancellationToken: ct)
            .WithRetryMillis(r => r == 5000)
            .AtLeast(1);
    }

    [Test]
    public async Task Stream_WithData_AndAtMost_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(TwoPrices));

        await Assert.That(stream).HasSseEvent("price", cancellationToken: ct)
            .WithData(d => string.Equals(d, "20", StringComparison.Ordinal))
            .AtMost(1);
    }

    [Test]
    public async Task Stream_WithDataParsedAs_AndId_Combined_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(TwoPrices));

        await Assert.That(stream).HasSseEvent("price", cancellationToken: ct)
            .WithDataParsedAs(ParseInt, v => v >= 20)
            .WithId("b")
            .Exactly(1);
    }

    [Test]
    public async Task Stream_WithRetryMillis_MatchingFrame_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: ping\ndata: x\nretry: 3000\n\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));

        await Assert.That(stream).HasSseEvent("ping", cancellationToken: ct)
            .WithRetryMillis(r => r == 3000)
            .AtLeast(1);
    }

    [Test]
    public async Task Stream_CancellationCut_WithNarrower_PrefersCancellationDiagnostic(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new SlowStream(TwoPrices);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var ex = await Assert.That(async () =>
        {
            // A narrower that can never be satisfied within the truncated read; the cancellation-cut
            // diagnostic must still take precedence over the narrower miss.
            await Assert.That(stream).HasSseEvent("price", cancellationToken: cts.Token)
                .WithId("never")
                .AtLeast(50);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("cancelled");
        await Assert.That(ex.Message).Contains("partial buffer");
    }

    [Test]
    public async Task Response_SourceThrows_FailsWithThrewMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Func<Task<HttpResponseMessage?>> throwingSource =
            () => throw new InvalidOperationException("boom");
        var ex = await Assert.That(async () =>
                await Assert.That(throwingSource).HasSseEvent("tick").AtLeast(1))
            .Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("InvalidOperationException");
        await Assert.That(ex.Message).Contains("boom");
    }

    [Test]
    public async Task Stream_SourceThrows_FailsWithThrewMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Func<Task<Stream?>> throwingSource =
            () => throw new InvalidOperationException("boom");
        var ex = await Assert.That(async () =>
                await Assert.That(throwingSource).HasSseEvent("tick").AtLeast(1))
            .Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("InvalidOperationException");
        await Assert.That(ex.Message).Contains("boom");
    }

    [Test]
    public async Task Response_EmptyBody_FailsWithEventNotFound(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(string.Empty, "text/event-stream");

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEvent("tick", cancellationToken: ct).AtLeast(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("but observed: 0");
    }

    [Test]
    public async Task Stream_ForeignCancellation_Propagates_NotReportedAsCut(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new ForeignCancelStream();

        // The supplied token (default/None) never fires; the stream throws an OperationCanceledException
        // of its own (modelling an HttpClient timeout). It must propagate, not be masked as a
        // cancellation-cut diagnostic.
        await Assert.That(async () => await Assert.That(stream).HasSseEvent("tick"))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Response_NoContentType_StrictFalse_ResolvesUtf8AndPasses(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(TwoPrices)));
        // No Content-Type header: ResolveEncoding's charset chain short-circuits to the UTF-8 fallback.
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        await Assert.That(response).HasSseEvent("price", strictContentType: false, cancellationToken: ct)
            .AtLeast(1);
    }

    private static HttpResponseMessage BuildResponse(string body, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new StreamContent(new MemoryStream(bytes));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>Emits the body one frame at a time with a sleep between frames so a short
    /// <c>CancelAfter</c> truncates the read partway through.</summary>
    private sealed class SlowStream : Stream
    {
        private readonly byte[] _buffer;
        private int _position;
        private readonly int _chunkSize;

        public SlowStream(string body)
        {
            _buffer = Encoding.UTF8.GetBytes(body);
            _chunkSize = body.IndexOf("\n\n", StringComparison.Ordinal) + 2;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _buffer.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _buffer.Length)
            {
                return 0;
            }

            var n = Math.Min(count, Math.Min(_chunkSize, _buffer.Length - _position));
            Array.Copy(_buffer, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _buffer.Length)
            {
                return 0;
            }

            if (_position > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }

            var n = Math.Min(buffer.Length, Math.Min(_chunkSize, _buffer.Length - _position));
            _buffer.AsSpan(_position, n).CopyTo(buffer.Span);
            _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Throws an <see cref="OperationCanceledException"/> that is not tied to the assertion's
    /// supplied token, modelling a foreign cancellation (for example an <c>HttpClient</c> timeout).</summary>
    private sealed class ForeignCancelStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new OperationCanceledException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
