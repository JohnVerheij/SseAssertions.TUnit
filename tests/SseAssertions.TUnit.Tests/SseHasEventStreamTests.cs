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

        await Assert.That(ex!.Message).Contains("Expected to find at least 5 event(s)");
        await Assert.That(ex.Message).Contains("but observed 3 event(s)");
    }

    [Test]
    public async Task HasSseEvent_StreamCanceled_PartialBufferStillEvaluated(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new ScriptedStream();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stream.Send("event: tick\ndata: 1\n\n");
        var pending = stream.NextPendingRead();
        var run = Task.Run(async () => await Assert.That(stream).HasSseEvent("tick", cancellationToken: cts.Token).Exactly(1), ct);

        // Cancel only once the reader has consumed the first event and waits for more: the one event that
        // arrived is what the canceled read evaluates, and it satisfies Exactly(1).
        await pending;
        await cts.CancelAsync();

        await run;
    }

    [Test]
    public async Task HasSseEvent_StreamCanceled_BelowMinCount_FailsWithCancellationCutRead(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new ScriptedStream();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stream.Send("event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\n");
        var pending = stream.NextPendingRead();
        var run = Task.Run(
            async () => await Assert.That(async () =>
            {
                await Assert.That(stream).HasSseEvent("tick", cancellationToken: cts.Token).AtLeast(100);
            }).Throws<AssertionException>(),
            ct);

        // Cancel only once the reader has consumed both events and waits for more.
        await pending;
        await cts.CancelAsync();
        var ex = await run;

        await Assert.That(ex!.Message).Contains("canceled");
        await Assert.That(ex.Message).Contains("partial buffer");
    }

    [Test]
    public async Task HasSseEvent_NullStream_FailsWithReceiverWasNull(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream nullStream = null!;
        var ex = await Assert.That(async () => await Assert.That(nullStream).HasSseEvent("tick", cancellationToken: ct))
            .Throws<AssertionException>();
        await Assert.That(ex!.Message).Contains("receiver was null");
    }

    [Test]
    public async Task HasSseEvent_NullEventName_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ThreeTicks));
        string nullName = null!;
        var ex = await Assert.That(async () => await Assert.That(stream).HasSseEvent(nullName, cancellationToken: ct))
            .Throws<ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task HasSseEvent_NegativeAtLeast_ThrowsArgumentOutOfRange(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ThreeTicks));
        var ex = await Assert.That(async () => await Assert.That(stream).HasSseEvent("tick", cancellationToken: ct).AtLeast(-1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(ex).IsNotNull();
    }
}
