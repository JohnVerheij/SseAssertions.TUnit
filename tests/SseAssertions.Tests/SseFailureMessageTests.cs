using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SseAssertions;

namespace SseAssertions.Tests;

/// <summary>
/// Framework-agnostic tests for the <see cref="SseFailureMessage"/> public factories. Covers the
/// happy-path output of each public factory plus argument-validation null guards.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseFailureMessageTests
{
    [Test]
    public async Task ParseFailure_IncludesTruncatedBody(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.ParseFailure("event: tick\ndata: 1\n\nnot a valid sse line");

        await Assert.That(msg).Contains("Server-Sent Events stream");
        await Assert.That(msg).Contains("body: event: tick");
    }

    [Test]
    public async Task ParseFailure_TruncatesBodyAt256Chars(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var longBody = new string('a', 500);
        var msg = SseFailureMessage.ParseFailure(longBody);

        await Assert.That(msg).Contains("…");
        await Assert.That(msg.Contains(new string('a', 500), StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ParseFailure_NullBody_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.ParseFailure(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task EventNotFound_RendersAllObservedEvents(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var available = new List<SseEvent>
        {
            new("ping", "alive"),
            new("heartbeat", string.Empty),
        };

        var msg = SseFailureMessage.EventNotFound("tick", available);

        await Assert.That(msg).Contains("\"tick\"");
        await Assert.That(msg).Contains("event=ping");
        await Assert.That(msg).Contains("event=heartbeat");
    }

    [Test]
    public async Task EventNotFound_TruncatesPerEventDataAt80Chars(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var available = new List<SseEvent>
        {
            new("update", new string('x', 200)),
        };

        var msg = SseFailureMessage.EventNotFound("tick", available);

        await Assert.That(msg).Contains("…");
        await Assert.That(msg.Contains(new string('x', 200), StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task EventNotFound_EmptyAvailable_OmitsListSuffix(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.EventNotFound("tick", Array.Empty<SseEvent>());

        await Assert.That(msg).Contains("0 total event");
    }

    [Test]
    public async Task EventNotFound_NullEventName_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.EventNotFound(null!, Array.Empty<SseEvent>()))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task EventNotFound_NullAvailable_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.EventNotFound("tick", null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task EventCountMismatch_AtLeast_RendersAtLeastLabel(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.EventCountMismatch("tick", expected: 3, actual: 2, SseCountComparison.AtLeast);

        await Assert.That(msg).Contains("at least 3");
        await Assert.That(msg).Contains("but observed: 2");
    }

    [Test]
    public async Task EventCountMismatch_AtMost_RendersAtMostLabel(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.EventCountMismatch("tick", expected: 1, actual: 5, SseCountComparison.AtMost);

        await Assert.That(msg).Contains("at most 1");
    }

    [Test]
    public async Task EventCountMismatch_Exactly_RendersExactlyLabel(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.EventCountMismatch("tick", expected: 2, actual: 3, SseCountComparison.Exactly);

        await Assert.That(msg).Contains("exactly 2");
    }

    [Test]
    public async Task EventCountMismatch_UnknownComparisonValue_FallsBackToExactlyLabel(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.EventCountMismatch("tick", expected: 2, actual: 3, (SseCountComparison)999);

        await Assert.That(msg).Contains("exactly 2");
    }

    [Test]
    public async Task EventCountMismatch_NullEventName_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.EventCountMismatch(null!, 1, 0, SseCountComparison.AtLeast))
            .Throws<ArgumentNullException>();
    }

    private static readonly string[] DataValuesSample = ["1", "2", "3"];

    [Test]
    public async Task DataPredicateNotMatched_ListsObservedDataValues(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.DataPredicateNotMatched("tick", DataValuesSample);

        await Assert.That(msg).Contains("\"tick\"");
        await Assert.That(msg).Contains("data: \"1\"");
        await Assert.That(msg).Contains("data: \"2\"");
        await Assert.That(msg).Contains("data: \"3\"");
    }

    [Test]
    public async Task DataPredicateNotMatched_TruncatesEachDataValueAt80Chars(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bigData = new string('a', 200);

        var msg = SseFailureMessage.DataPredicateNotMatched("tick", new[] { bigData });

        await Assert.That(msg).Contains("…");
        await Assert.That(msg.Contains(bigData, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task DataPredicateNotMatched_NullEventName_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.DataPredicateNotMatched(null!, Array.Empty<string>()))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DataPredicateNotMatched_NullDataValues_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.DataPredicateNotMatched("tick", null!))
            .Throws<ArgumentNullException>();
    }

    private static readonly string?[] IdValuesSample = ["a", "b", null];

    [Test]
    public async Task IdNotMatched_ListsObservedIdValuesIncludingAbsent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.IdNotMatched("tick", "z", IdValuesSample);

        await Assert.That(msg).Contains("\"tick\"");
        await Assert.That(msg).Contains("with id \"z\"");
        await Assert.That(msg).Contains("none carried that id");
        await Assert.That(msg).Contains("id: \"a\"");
        await Assert.That(msg).Contains("id: \"b\"");
        await Assert.That(msg).Contains("id: <absent>");
    }

    [Test]
    public async Task IdNotMatched_EmptyIdValues_OmitsList(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.IdNotMatched("tick", "z", Array.Empty<string?>());

        await Assert.That(msg).Contains("none carried that id");
        await Assert.That(msg.Contains("id: ", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task IdNotMatched_NullArguments_ThrowArgumentNullException(int whichNull, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.IdNotMatched(
                whichNull == 0 ? null! : "tick",
                whichNull == 1 ? null! : "z",
                whichNull == 2 ? null! : Array.Empty<string?>()))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DataDeserializationFailed_IncludesExceptionTypeMessageAndTruncatedData(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var inner = new InvalidOperationException("the json was malformed");

        var msg = SseFailureMessage.DataDeserializationFailed("order-update", "{\"bad", inner);

        await Assert.That(msg).Contains("InvalidOperationException");
        await Assert.That(msg).Contains("the json was malformed");
        await Assert.That(msg).Contains("order-update");
    }

    [Test]
    public async Task DataDeserializationFailed_NullArguments_ThrowArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.DataDeserializationFailed(null!, "x", new InvalidOperationException()))
            .Throws<ArgumentNullException>();
        await Assert.That(() => SseFailureMessage.DataDeserializationFailed("x", null!, new InvalidOperationException()))
            .Throws<ArgumentNullException>();
        await Assert.That(() => SseFailureMessage.DataDeserializationFailed("x", "y", null!))
            .Throws<ArgumentNullException>();
    }

    private static readonly int?[] RetryValuesMixed = [100, null, 5000];

    [Test]
    public async Task RetryMillisPredicateNotMatched_RendersAbsentForNullValues(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.RetryMillisPredicateNotMatched("tick", RetryValuesMixed);

        await Assert.That(msg).Contains("retry: 100");
        await Assert.That(msg).Contains("retry: <absent>");
        await Assert.That(msg).Contains("retry: 5000");
    }

    [Test]
    public async Task RetryMillisPredicateNotMatched_NullEventName_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.RetryMillisPredicateNotMatched(null!, Array.Empty<int?>()))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task RetryMillisPredicateNotMatched_NullRetryValues_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.RetryMillisPredicateNotMatched("tick", null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task UnexpectedContentType_NonNullActual_RendersValue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.UnexpectedContentType("application/json");

        await Assert.That(msg).Contains("\"text/event-stream\"");
        await Assert.That(msg).Contains("application/json");
    }

    [Test]
    public async Task UnexpectedContentType_NullActual_RendersAbsentToken(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.UnexpectedContentType(null);

        await Assert.That(msg).Contains("<absent>");
    }

    [Test]
    public async Task CancellationCutRead_IncludesByteAndEventCounts(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var msg = SseFailureMessage.CancellationCutRead(1247, 3, "event: tick\ndata: 1");

        await Assert.That(msg).Contains("1247 byte");
        await Assert.That(msg).Contains("3 event");
        await Assert.That(msg).Contains("event: tick");
    }

    [Test]
    public async Task CancellationCutRead_TruncatesExcerptAt256Chars(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var longExcerpt = new string('z', 500);

        var msg = SseFailureMessage.CancellationCutRead(500, 0, longExcerpt);

        await Assert.That(msg).Contains("…");
        await Assert.That(msg.Contains(longExcerpt, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task CancellationCutRead_NullExcerpt_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseFailureMessage.CancellationCutRead(0, 0, null!))
            .Throws<ArgumentNullException>();
    }
}
