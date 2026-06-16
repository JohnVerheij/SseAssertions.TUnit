using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions.Exceptions;

namespace SseAssertions.TUnit.Tests;

/// <summary>
/// End-to-end tests for the <c>HasSseEvent(eventName, cancellationToken)</c> chain entry point on
/// the <see cref="Stream"/> receiver. Covers the happy-path body read, the count terminator's fail
/// path, and cancellation-bounded partial-buffer semantics.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseHasEventStreamTests
{
    private const string ThreeTicks = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\nevent: tick\ndata: 3\n\n";

    [Test]
    public async Task HasSseEvent_StreamWithMatchingEvents_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ThreeTicks));

        await Assert.That(stream).HasSseEvent("tick", cancellationToken: ct).AtLeast(2);
    }

    [Test]
    public async Task HasSseEvent_StreamWithFewerThanRequested_FailsWithCountMismatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ThreeTicks));

        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseEvent("tick", cancellationToken: ct).AtLeast(5);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("at least 5");
        await Assert.That(ex.Message).Contains("but observed: 3");
    }

    [Test]
    public async Task HasSseEvent_StreamCancelled_PartialBufferStillEvaluated(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new BlockingStream(ThreeTicks);
        using var cts = new CancellationTokenSource();

        // Cancel after a brief moment; the partial buffer is what the BlockingStream emitted up
        // to that point. The first event ("tick"/1) is fully emitted before cancellation.
        cts.CancelAfter(System.TimeSpan.FromMilliseconds(50));

        await Assert.That(stream).HasSseEvent("tick", cancellationToken: cts.Token).AtLeast(1);
    }

    [Test]
    public async Task HasSseEvent_StreamCancelled_BelowMinCount_FailsWithCancellationCutRead(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new BlockingStream(ThreeTicks);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(System.TimeSpan.FromMilliseconds(50));

        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseEvent("tick", cancellationToken: cts.Token).AtLeast(100);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("cancelled");
        await Assert.That(ex.Message).Contains("partial buffer");
    }

    [Test]
    public async Task HasSseEvent_NullStream_FailsWithReceiverWasNull(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream nullStream = null!;
        var ex = await Assert.That(async () => await Assert.That(nullStream).HasSseEvent("tick"))
            .Throws<AssertionException>();
        await Assert.That(ex!.Message).Contains("receiver was null");
    }

    [Test]
    public async Task HasSseEvent_NullEventName_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ThreeTicks));
        string nullName = null!;
        var ex = await Assert.That(async () => await Assert.That(stream).HasSseEvent(nullName))
            .Throws<ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task HasSseEvent_NegativeAtLeast_ThrowsArgumentOutOfRange(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ThreeTicks));
        var ex = await Assert.That(async () => await Assert.That(stream).HasSseEvent("tick").AtLeast(-1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(ex).IsNotNull();
    }

    /// <summary>Streams the supplied SSE body one frame at a time with a brief sleep between
    /// frames, simulating a finite-frame SSE server. Used to verify cancellation-bounded read
    /// semantics: a cancellation token firing mid-stream lets the adapter capture whatever
    /// frames arrived before the cut.</summary>
    private sealed class BlockingStream : Stream
    {
        private readonly byte[] _buffer;
        private int _position;
        private readonly int _chunkSize;

        public BlockingStream(string body)
        {
            _buffer = Encoding.UTF8.GetBytes(body);
            _chunkSize = body.IndexOf("\n\n", System.StringComparison.Ordinal) + 2;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _buffer.Length;

        public override long Position
        {
            get => _position;
            set => throw new System.NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadCore(buffer, offset, count);

        public override async ValueTask<int> ReadAsync(System.Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _buffer.Length)
            {
                return 0;
            }

            // Sleep between frames so a CancelAfter(50ms) catches the second frame mid-emit.
            if (_position > 0)
            {
                await Task.Delay(System.TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }

            var bytesToRead = System.Math.Min(
                buffer.Length,
                System.Math.Min(_chunkSize, _buffer.Length - _position));
            _buffer.AsSpan(_position, bytesToRead).CopyTo(buffer.Span);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new System.NotSupportedException();

        public override void SetLength(long value) => throw new System.NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new System.NotSupportedException();

        private int ReadCore(byte[] buffer, int offset, int count)
        {
            if (_position >= _buffer.Length)
            {
                return 0;
            }

            var bytesToRead = System.Math.Min(count, System.Math.Min(_chunkSize, _buffer.Length - _position));
            System.Array.Copy(_buffer, _position, buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }
    }
}
