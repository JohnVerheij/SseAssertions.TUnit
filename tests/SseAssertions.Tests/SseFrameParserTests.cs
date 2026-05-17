using System.Threading;
using System.Threading.Tasks;
using SseAssertions;

namespace SseAssertions.Tests;

/// <summary>
/// Framework-agnostic tests for <see cref="SseFrameParser.Parse(string)"/>. Covers the WHATWG /
/// W3C SSE wire-format rules: line terminators, BOM handling, event dispatch on blank lines,
/// the <c>"message"</c> default event-name, multi-line data accumulation, comment-line ignoring,
/// retry-field parsing, unknown-field tolerance, and argument validation.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseFrameParserTests
{
    [Test]
    public async Task Parse_NullBody_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFrameParser.Parse(null!)).Throws<System.ArgumentNullException>();
    }

    [Test]
    public async Task Parse_EmptyBody_ReturnsEmptyList(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse(string.Empty);

        await Assert.That(events).IsEmpty();
    }

    [Test]
    public async Task Parse_SingleFrame_ProducesOneEventWithDefaultName(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("data: hello\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].EventName).IsEqualTo("message");
        await Assert.That(events[0].Data).IsEqualTo("hello");
    }

    [Test]
    public async Task Parse_MultipleFrames_ProducesEventsInDocumentOrder(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var body = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\nevent: tick\ndata: 3\n\n";
        var events = SseFrameParser.Parse(body);

        await Assert.That(events.Count).IsEqualTo(3);
        await Assert.That(events[0].Data).IsEqualTo("1");
        await Assert.That(events[1].Data).IsEqualTo("2");
        await Assert.That(events[2].Data).IsEqualTo("3");
    }

    [Test]
    public async Task Parse_MultiDataLines_JoinsWithLineFeed(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("data: line-1\ndata: line-2\ndata: line-3\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo("line-1\nline-2\nline-3");
    }

    [Test]
    public async Task Parse_IdField_CapturedOnEvent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("id: 42\ndata: hello\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Id).IsEqualTo("42");
    }

    [Test]
    public async Task Parse_RetryField_CapturedAsInteger(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("retry: 5000\ndata: ping\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].RetryMillis).IsEqualTo(5000);
    }

    [Test]
    public async Task Parse_CommentLine_Ignored(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse(": this is a heartbeat\ndata: hello\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo("hello");
    }

    [Test]
    public async Task Parse_Utf8BomAtOffsetZero_IsConsumed(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("\uFEFFdata: hello\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo("hello");
    }

    [Test]
    public async Task Parse_BareCrLineTerminator_HandledAsLineEnd(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("data: hello\r\r");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo("hello");
    }

    [Test]
    public async Task Parse_CrLfLineTerminator_HandledAsSingleLineEnd(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("data: hello\r\n\r\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo("hello");
    }

    [Test]
    public async Task Parse_ExplicitEventName_OverridesMessageDefault(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("event: tick\ndata: 1\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].EventName).IsEqualTo("tick");
    }

    [Test]
    public async Task Parse_NoTrailingNewline_LastEventStillDispatched(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("event: tick\ndata: 1");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].EventName).IsEqualTo("tick");
        await Assert.That(events[0].Data).IsEqualTo("1");
    }

    [Test]
    public async Task Parse_RetryNonNumericValue_FieldIgnored(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("retry: not-a-number\ndata: ping\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].RetryMillis).IsNull();
    }

    [Test]
    public async Task Parse_RetryNegativeValue_FieldIgnored(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("retry: -1\ndata: ping\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].RetryMillis).IsNull();
    }

    [Test]
    public async Task Parse_UnknownField_IgnoredButOtherFieldsCaptured(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("foo: bar\ndata: hello\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo("hello");
    }

    [Test]
    public async Task Parse_CommentOnlyBody_ProducesNoEvents(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse(": keepalive\n\n: another\n\n");

        await Assert.That(events).IsEmpty();
    }

    [Test]
    public async Task Parse_BlankLinesOnly_ProducesNoEvents(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("\n\n\n");

        await Assert.That(events).IsEmpty();
    }

    [Test]
    public async Task Parse_MidStreamBomBytes_TreatedAsFieldContent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Only the BOM at offset 0 is consumed; a U+FEFF appearing later passes through as a
        // regular character in the field value.
        var events = SseFrameParser.Parse("data: a\uFEFFb\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo("a\uFEFFb");
    }

    [Test]
    public async Task Parse_BomOnlyInput_ProducesNoEvents(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("\uFEFF");

        await Assert.That(events).IsEmpty();
    }

    [Test]
    public async Task Parse_FieldWithoutColon_TreatedAsFieldNameWithEmptyValue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Per spec: a line without a colon is interpreted as a field with the line as its
        // name and an empty value. "data" with empty value still triggers the data branch.
        var events = SseFrameParser.Parse("data\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Parse_DataLeadingSpace_StrippedOnce(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // "data:  hello" (two leading spaces) -- only first space stripped per spec.
        var events = SseFrameParser.Parse("data:  hello\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo(" hello");
    }

    [Test]
    public async Task Parse_FrameWithOnlyIdAndRetry_NotDispatchedPerSpec(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Per WHATWG spec: a frame with no data: directive is dropped on blank-line dispatch.
        var events = SseFrameParser.Parse("id: 42\nretry: 5000\n\n");

        await Assert.That(events).IsEmpty();
    }

    [Test]
    public async Task Parse_EmptyDataLine_ProducesEventWithEmptyData(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A frame whose only directive is an empty data: line dispatches with Data=string.Empty
        // per spec (the data buffer is non-empty after the LF append, then the trailing LF
        // gets stripped, leaving an empty string).
        var events = SseFrameParser.Parse("data:\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Data).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Parse_SubsequentRetryOverwritesPrevious_WithinSameFrame(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = SseFrameParser.Parse("retry: 100\nretry: 200\ndata: ping\n\n");

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].RetryMillis).IsEqualTo(200);
    }
}
