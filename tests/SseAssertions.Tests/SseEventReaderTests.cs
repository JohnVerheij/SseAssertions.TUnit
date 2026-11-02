using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SseAssertions;

namespace SseAssertions.Tests;

/// <summary>
/// Tests for <see cref="SseEventReader"/>: incremental parsing as chunks arrive, with the same result as
/// <see cref="SseFrameParser.Parse"/> however the body is split, early stopping, and the diagnostics it keeps.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseEventReaderTests
{
    /// <summary>Bodies covering every parsing rule: terminators, BOM, comments, dataless frames, multi-line data,
    /// ids, retries, unknown fields, multi-byte characters, and an unterminated final frame.</summary>
    private static readonly string[] Corpus =
    [
        "data: hello\n\n",
        "\uFEFFevent: tick\ndata: 1\n\nevent: tick\ndata: 2\n\n",
        "retry: 5000\n\ndata: after-retry\n\n",
        ": comment\nevent: a\ndata: x\nid: 7\nretry: 100\n\n",
        "data: one\r\ndata: two\r\n\r\nevent: b\rdata: three\r\r",
        "data: caf\u00e9 \ud83d\ude00\n\ndata: tail without blank line",
        "event: only-name\n\nid: 1\n\nunknown: field\ndata\n\n",
        "id: a\0b\ndata: nul id ignored\nretry: 12x\n\n",
        "\n\n\ndata: after blank lines\n\n",
        string.Empty,
    ];

    /// <summary>Reading the corpus whole, a few bytes at a time, and one byte at a time (every possible split,
    /// including inside a CRLF, a multi-byte character, and the byte order mark) yields what
    /// <see cref="SseFrameParser.Parse"/> yields.</summary>
    [Test]
    public async Task ReadAsync_AnyChunking_MatchesTheStringParser(CancellationToken cancellationToken)
    {
        foreach (var body in Corpus)
        {
            var expected = SseFrameParser.Parse(body);
            var bytes = Encoding.UTF8.GetBytes(body);
            foreach (var chunkSize in new[] { bytes.Length + 1, 3, 1 })
            {
                var actual = await ReadAllAsync(new ChunkedStream(bytes, chunkSize), cancellationToken);
                await Assert.That(actual).IsEquivalentTo(expected);
            }
        }
    }

    /// <summary>An event is returned as soon as its frame is complete, before the stream has more.</summary>
    [Test]
    public async Task ReadAsync_ReturnsEachEventWhenItsFrameCompletes(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        var reader = new SseEventReader(stream);

        stream.Send("event: tick\nda");
        var first = StartRead(reader, cancellationToken);
        await Assert.That(first.IsCompleted).IsFalse();

        stream.Send("ta: 1\n\nevent: tick\n");
        await Assert.That((await first)!.Data).IsEqualTo("1");

        var second = StartRead(reader, cancellationToken);
        stream.Send("data: 2\n\n");
        await Assert.That((await second)!.Data).IsEqualTo("2");

        stream.Complete();
        await Assert.That(await reader.ReadAsync(cancellationToken)).IsNull();
        await Assert.That(await reader.ReadAsync(cancellationToken)).IsNull();
    }

    /// <summary>A CR that ends one chunk and the LF that starts the next form one CRLF, not two line ends.</summary>
    [Test]
    public async Task ReadAsync_CrlfSplitAcrossChunks_IsOneTerminator(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        var reader = new SseEventReader(stream);

        stream.Send("data: a\r");
        stream.Send("\ndata: b\r\n\r\n");
        stream.Complete();

        var evt = await reader.ReadAsync(cancellationToken);
        await Assert.That(evt!.Data).IsEqualTo("a\nb");
        await Assert.That(await reader.ReadAsync(cancellationToken)).IsNull();
    }

    /// <summary>A line longer than the internal buffer is read whole.</summary>
    [Test]
    public async Task ReadAsync_LineLongerThanTheBuffer_IsReadWhole(CancellationToken cancellationToken)
    {
        var payload = new string('x', 20_000);
        var events = await ReadAllAsync(new ChunkedStream(Encoding.UTF8.GetBytes($"data: {payload}\n\n"), 1_000), cancellationToken);

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo(payload);
    }

    /// <summary>A byte order mark split over two chunks is still skipped.</summary>
    [Test]
    public async Task ReadAsync_ByteOrderMarkSplitAcrossChunks_IsSkipped(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        var reader = new SseEventReader(stream);

        stream.Send([0xEF]);
        stream.Send([0xBB, 0xBF]);
        stream.Send("data: x\n\n");

        await Assert.That((await reader.ReadAsync(cancellationToken))!.Data).IsEqualTo("x");
    }

    /// <summary>A stream that starts with a lone 0xEF byte that is not a byte order mark keeps that byte.</summary>
    [Test]
    public async Task ReadAsync_PartialByteOrderMarkPrefixAtEnd_IsKept(CancellationToken cancellationToken)
    {
        var events = await ReadAllAsync(new ChunkedStream([0xEF], 1), cancellationToken);

        await Assert.That(events).IsEmpty();
    }

    /// <summary>A canceled wait throws, and the reader continues where it was when read again.</summary>
    [Test]
    public async Task ReadAsync_Canceled_ThrowsAndCanContinue(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        var reader = new SseEventReader(stream);
        stream.Send("data: par");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = StartRead(reader, cts.Token);
        await cts.CancelAsync();
        var canceled = false;
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        await Assert.That(canceled).IsTrue();

        stream.Send("tial\n\n");
        await Assert.That((await reader.ReadAsync(cancellationToken))!.Data).IsEqualTo("partial");
    }

    /// <summary>The reader counts the bytes it read and keeps the start of the decoded text.</summary>
    [Test]
    public async Task BytesReadAndTextExcerpt_DescribeWhatWasRead(CancellationToken cancellationToken)
    {
        var body = "event: tick\ndata: 1\n\n";
        var reader = new SseEventReader(new ChunkedStream(Encoding.UTF8.GetBytes(body), 4));

        await Assert.That(reader.BytesRead).IsEqualTo(0);
        _ = await reader.ReadAsync(cancellationToken);

        await Assert.That(reader.BytesRead).IsEqualTo(body.Length);
        await Assert.That(reader.TextExcerpt).IsEqualTo("event: tick\ndata: 1\n\n");
    }

    /// <summary>The excerpt stops growing after about 256 characters.</summary>
    [Test]
    public async Task TextExcerpt_IsBounded(CancellationToken cancellationToken)
    {
        var body = string.Concat(Enumerable.Repeat("data: 0123456789012345678901234567890123456789\n\n", 50));
        var reader = new SseEventReader(new MemoryStream(Encoding.UTF8.GetBytes(body)));

        while (await reader.ReadAsync(cancellationToken) is not null)
        {
            // Drain.
        }

        await Assert.That(reader.TextExcerpt.Length).IsLessThan(320);
        await Assert.That(reader.BytesRead).IsEqualTo(body.Length);
    }

    /// <summary>A null stream is rejected.</summary>
    [Test]
    public async Task Constructor_NullStream_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => new SseEventReader(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>Starts reading the next event, as a task the test can inspect before awaiting it.</summary>
    private static Task<SseEvent?> StartRead(SseEventReader reader, CancellationToken cancellationToken)
        => reader.ReadAsync(cancellationToken).AsTask();

    /// <summary>Reads every event of <paramref name="stream"/>.</summary>
    private static async Task<List<SseEvent>> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        var reader = new SseEventReader(stream);
        var events = new List<SseEvent>();
        while (await reader.ReadAsync(cancellationToken) is { } evt)
        {
            events.Add(evt);
        }

        return events;
    }
}
