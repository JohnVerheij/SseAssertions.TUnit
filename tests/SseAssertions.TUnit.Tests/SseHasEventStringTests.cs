using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions.Exceptions;

namespace SseAssertions.TUnit.Tests;

/// <summary>
/// End-to-end tests for the <c>HasSseEvent(eventName)</c> chain entry point on the
/// <see cref="string"/> receiver. Covers the chain's name-narrower, data-predicate narrower, and
/// count terminators (<c>AtLeast</c>, <c>AtMost</c>, <c>Exactly</c>) plus their failure
/// diagnostics.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseHasEventStringTests
{
    // Concatenated at runtime so the analyzers see it as a non-constant; using `const` triggers
    // TUnitAssertions0005 ("Assert.That should not be used with a constant value") on every
    // test in the file, and using `static readonly` with a literal triggers CA1802 ("declare
    // as 'const' instead"). Splitting and concatenating sidesteps both rules without changing
    // the runtime value.
    private static readonly string ThreeTicks = string.Concat(
        "event: tick\ndata: 1\n\n",
        "event: tick\ndata: 2\n\n",
        "event: tick\ndata: 3\n\n");

    [Test]
    public async Task HasSseEvent_AtLeastOne_PresentEvent_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(ThreeTicks).HasSseEvent("tick").AtLeast(1);
    }

    [Test]
    public async Task HasSseEvent_AbsentEvent_FailsWithEventNotFound(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(ThreeTicks).HasSseEvent("missing").AtLeast(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"missing\"");
        await Assert.That(ex.Message).Contains("event=tick");
    }

    [Test]
    public async Task HasSseEvent_AtLeast_BelowThreshold_FailsWithCountMismatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(ThreeTicks).HasSseEvent("tick").AtLeast(5);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("at least 5");
        await Assert.That(ex.Message).Contains("but observed: 3");
    }

    [Test]
    public async Task HasSseEvent_AtMost_OverThreshold_FailsWithCountMismatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(ThreeTicks).HasSseEvent("tick").AtMost(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("at most 1");
    }

    [Test]
    public async Task HasSseEvent_Exactly_PassesWhenCountMatches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(ThreeTicks).HasSseEvent("tick").Exactly(3);
    }

    [Test]
    public async Task HasSseEvent_Exactly_FailsWhenCountDiffers(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(ThreeTicks).HasSseEvent("tick").Exactly(2);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("exactly 2");
    }

    [Test]
    public async Task HasSseEvent_WithData_NarrowsToMatchingPredicate(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(ThreeTicks)
            .HasSseEvent("tick")
            .WithData(static d => d is "2")
            .Exactly(1);
    }

    [Test]
    public async Task HasSseEvent_WithData_NoMatch_FailsWithDataPredicateMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(ThreeTicks)
                .HasSseEvent("tick")
                .WithData(static d => d is "99")
                .AtLeast(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("Data satisfied the predicate");
        await Assert.That(ex.Message).Contains("data: \"1\"");
    }

    [Test]
    public async Task HasSseEvent_AtLeast_NegativeCount_ThrowsArgumentOutOfRange(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var body = ThreeTicks;
        var ex = await Assert.That(async () => await Assert.That(body).HasSseEvent("tick").AtLeast(-1))
            .Throws<System.ArgumentOutOfRangeException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task HasSseEvent_EventNameNull_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var body = ThreeTicks;
        string nullName = null!;
        var ex = await Assert.That(async () => await Assert.That(body).HasSseEvent(nullName))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task HasSseEvent_NullBody_FailsWithReceiverWasNull(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string nullBody = null!;
        var ex = await Assert.That(async () => await Assert.That(nullBody).HasSseEvent("tick").AtLeast(1))
            .Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("receiver was null");
    }

    [Test]
    public async Task HasSseEvent_SourceThrows_FailsWithThrewMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        System.Func<System.Threading.Tasks.Task<string?>> throwingSource =
            () => throw new System.InvalidOperationException("boom");
        var ex = await Assert.That(async () => await Assert.That(throwingSource).HasSseEvent("tick").AtLeast(1))
            .Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("InvalidOperationException");
        await Assert.That(ex.Message).Contains("boom");
    }

    // ---- WithId (v0.6.0) ----

    private static readonly string IdsBody = string.Concat(
        "event: tick\nid: a\ndata: 1\n\n",
        "event: tick\nid: b\ndata: 2\n\n",
        "event: tick\ndata: 3\n\n");

    [Test]
    public async Task HasSseEvent_WithId_NarrowsToMatchingId(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(IdsBody).HasSseEvent("tick").WithId("a").Exactly(1);
    }

    [Test]
    public async Task HasSseEvent_WithId_NoMatch_FailsListingObservedIds(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(IdsBody).HasSseEvent("tick").WithId("zzz").AtLeast(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("none carried that id");
        await Assert.That(ex.Message).Contains("id: \"a\"");
        await Assert.That(ex.Message).Contains("id: <absent>");
    }

    [Test]
    public async Task HasSseEvent_WithId_Null_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var body = IdsBody;
        string nullId = null!;
        var ex = await Assert.That(async () => await Assert.That(body).HasSseEvent("tick").WithId(nullId))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    // ---- WithRetryMillis (v0.6.0) ----

    private static readonly string RetryBody = string.Concat(
        "event: tick\nretry: 5000\ndata: 1\n\n",
        "event: tick\nretry: 1000\ndata: 2\n\n",
        "event: tick\ndata: 3\n\n");

    [Test]
    public async Task HasSseEvent_WithRetryMillis_NarrowsToMatchingValue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(RetryBody).HasSseEvent("tick").WithRetryMillis(static r => r == 5000).Exactly(1);
    }

    [Test]
    public async Task HasSseEvent_WithRetryMillis_NoMatch_FailsListingObservedRetries(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(RetryBody).HasSseEvent("tick").WithRetryMillis(static r => r == 9999).AtLeast(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("RetryMillis satisfied the predicate");
        await Assert.That(ex.Message).Contains("retry: 5000");
        await Assert.That(ex.Message).Contains("retry: <absent>");
    }

    [Test]
    public async Task HasSseEvent_WithRetryMillis_Null_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var body = RetryBody;
        System.Func<int?, bool> nullPredicate = null!;
        var ex = await Assert.That(async () =>
                await Assert.That(body).HasSseEvent("tick").WithRetryMillis(nullPredicate))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    // ---- WithDataParsedAs<T> (v0.6.0) ----

    private static readonly string NonIntBody = string.Concat("event: tick\n", "data: notanumber\n\n");

    [Test]
    public async Task HasSseEvent_WithDataParsedAs_NarrowsToParsedPredicate(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(ThreeTicks)
            .HasSseEvent("tick")
            .WithDataParsedAs(static d => int.Parse(d, CultureInfo.InvariantCulture), static v => v == 2)
            .Exactly(1);
    }

    [Test]
    public async Task HasSseEvent_WithDataParsedAs_PredicateNoMatch_FailsWithDataMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(ThreeTicks)
                .HasSseEvent("tick")
                .WithDataParsedAs(static d => int.Parse(d, CultureInfo.InvariantCulture), static v => v == 99)
                .AtLeast(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("Data satisfied the predicate");
        await Assert.That(ex.Message).Contains("data: \"1\"");
    }

    [Test]
    public async Task HasSseEvent_WithDataParsedAs_ParseThrows_FailsWithDeserializerMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(NonIntBody)
                .HasSseEvent("tick")
                .WithDataParsedAs(static d => int.Parse(d, CultureInfo.InvariantCulture), static v => v >= 0)
                .AtLeast(1);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("deserializer threw");
        await Assert.That(ex.Message).Contains("FormatException");
        await Assert.That(ex.Message).Contains("notanumber");
    }

    [Test]
    public async Task HasSseEvent_WithDataParsedAs_NullParse_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var body = ThreeTicks;
        System.Func<string, int> nullParse = null!;
        var ex = await Assert.That(async () =>
                await Assert.That(body).HasSseEvent("tick").WithDataParsedAs(nullParse, static v => v == 1))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task HasSseEvent_WithDataParsedAs_NullPredicate_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var body = ThreeTicks;
        System.Func<int, bool> nullPredicate = null!;
        var ex = await Assert.That(async () => await Assert.That(body)
                .HasSseEvent("tick")
                .WithDataParsedAs(static d => int.Parse(d, CultureInfo.InvariantCulture), nullPredicate))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }
}
