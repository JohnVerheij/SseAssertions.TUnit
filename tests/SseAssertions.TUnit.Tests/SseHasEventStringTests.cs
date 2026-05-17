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
}
