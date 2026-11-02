using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions.Exceptions;

namespace SseAssertions.TUnit.Tests;

/// <summary>
/// Tests for <c>HasSseEventIds()</c> on the <see cref="string"/>, <see cref="Stream"/>, and
/// <see cref="System.Net.Http.HttpResponseMessage"/> receivers: every event carries an id, optionally in strictly
/// increasing or sequential integer order, within an optional <c>Until</c> window. Failures list the ids seen.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class HasSseEventIdsTests
{
    private const string Sequential = "id: 1\ndata: a\n\nid: 2\ndata: b\n\nid: 3\ndata: c\n\n";

    /// <summary>Ids that are present, increasing, and sequential pass every form.</summary>
    [Test]
    public async Task SequentialIds_PassEveryForm(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(Sequential).HasSseEventIds();
        await Assert.That(Sequential).HasSseEventIds().StrictlyIncreasing();
        await Assert.That(Sequential).HasSseEventIds().Sequential();
        await Assert.That(Sequential).HasSseEventIds().Sequential(startingAt: 1);
    }

    /// <summary>An event without an id fails, naming it and listing the ids.</summary>
    [Test]
    public async Task MissingId_FailsNamingTheEvent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exception = await Assert.That(async () =>
        {
            await Assert.That("id: 1\ndata: a\n\nevent: tick\ndata: b\n\n").HasSseEventIds();
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("every event to carry an id");
        await Assert.That(exception.Message).Contains("event 2 (tick) carries no id");
        await Assert.That(exception.Message).Contains("ids: \"1\", <absent>");
    }

    /// <summary>A non-integer id fails an ordered form; a repeated id fails <c>StrictlyIncreasing</c>.</summary>
    [Test]
    public async Task StrictlyIncreasing_NonIntegerOrRepeated_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nonInteger = await Assert.That(async () =>
        {
            await Assert.That("id: 1\ndata: a\n\nid: abc\ndata: b\n\n").HasSseEventIds().StrictlyIncreasing();
        }).Throws<AssertionException>();
        await Assert.That(nonInteger!.Message).Contains("event 2 has the id \"abc\", which is not an integer");

        var repeated = await Assert.That(async () =>
        {
            await Assert.That("id: 5\ndata: a\n\nid: 5\ndata: b\n\n").HasSseEventIds().StrictlyIncreasing();
        }).Throws<AssertionException>();
        await Assert.That(repeated!.Message).Contains("every event to carry an integer id, increasing strictly");
        await Assert.That(repeated.Message).Contains("event 2 has the id 5, not greater than the previous id 5");

        await Assert.That("id: 2\ndata: a\n\nid: 9\ndata: b\n\n").HasSseEventIds().StrictlyIncreasing();
    }

    /// <summary>A gap fails <c>Sequential</c>; a wrong first id fails <c>Sequential(startingAt)</c>.</summary>
    [Test]
    public async Task Sequential_GapOrWrongStart_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var gap = await Assert.That(async () =>
        {
            await Assert.That("id: 1\ndata: a\n\nid: 3\ndata: b\n\n").HasSseEventIds().Sequential();
        }).Throws<AssertionException>();
        await Assert.That(gap!.Message).Contains("every event to carry an integer id, sequential");
        await Assert.That(gap.Message).Contains("event 2 has the id 3 where 2 was expected");

        var start = await Assert.That(async () =>
        {
            await Assert.That(Sequential).HasSseEventIds().Sequential(startingAt: 0);
        }).Throws<AssertionException>();
        await Assert.That(start!.Message).Contains("sequential from 0");
        await Assert.That(start.Message).Contains("event 1 has the id 1 where 0 was expected");

        await Assert.That("id: 7\ndata: a\n\nid: 8\ndata: b\n\n").HasSseEventIds().Sequential();
    }

    /// <summary>A body without events fails: there is nothing to vouch for.</summary>
    [Test]
    public async Task NoEvents_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(": only a comment\n\n").HasSseEventIds();
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("but no event arrived");
    }

    /// <summary><c>Until</c> ends the window; events after it are not checked.</summary>
    [Test]
    public async Task Until_EventsAfterTheWindowAreNotChecked(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Body = "id: 1\ndata: a\n\nid: 2\nevent: done\ndata: b\n\ndata: no id\n\n";

        await Assert.That(Body).HasSseEventIds().Sequential().Until("done");
        await Assert.That(async () =>
        {
            await Assert.That(Body).HasSseEventIds();
        }).Throws<AssertionException>();
    }

    /// <summary>More than 20 ids are listed up to 20, with the remainder counted.</summary>
    [Test]
    public async Task ManyIds_ListIsBounded(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = string.Concat(Enumerable.Range(1, 25).Select(i => $"id: {i}\ndata: x\n\n")) + "data: no id\n\n";

        var exception = await Assert.That(async () =>
        {
            await Assert.That(body).HasSseEventIds();
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("\"20\", \u2026 (6 more)");
    }

    /// <summary>The stream receiver stops at the first violation, so it fails on a stream that never ends.</summary>
    [Test]
    public async Task StreamReceiver_EndlessStream_FailsAtTheFirstViolation(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        stream.Send("id: 1\ndata: a\n\nid: 3\ndata: b\n\n");

        await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseEventIds(cancellationToken).Sequential();
        }).Throws<AssertionException>();
    }

    /// <summary>The stream receiver passes at the <c>Until</c> event on a stream that never ends.</summary>
    [Test]
    public async Task StreamReceiver_Until_PassesAtTheEvent(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        stream.Send("id: 1\ndata: a\n\nid: 2\nevent: done\ndata: b\n\n");

        await Assert.That(stream).HasSseEventIds(cancellationToken).StrictlyIncreasing().Until("done");
    }

    /// <summary>When the token ends the read before any event, the cancellation-cut diagnostic is reported.</summary>
    [Test]
    public async Task StreamReceiver_TokenEndsTheReadBeforeAnyEvent_ReportsTheCut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new ScriptedStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseEventIds(cts.Token);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("the read was canceled after 0 byte(s)");
    }

    /// <summary>The response receiver checks the content type unless told not to, and takes the token in every
    /// shape.</summary>
    [Test]
    public async Task ResponseReceiver_ContentTypeAndTokenShapes(CancellationToken cancellationToken)
    {
        using var ok = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes(Sequential)));
        using var plain = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes(Sequential)), "text/plain");
        using var lenient = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes(Sequential)), "text/plain");

        await Assert.That(ok).HasSseEventIds(cancellationToken).Sequential(startingAt: 1);
        var wrongType = await Assert.That(async () =>
        {
            await Assert.That(plain).HasSseEventIds(cancellationToken: cancellationToken);
        }).Throws<AssertionException>();
        await Assert.That(wrongType!.Message).Contains("text/plain");
        await Assert.That(lenient).HasSseEventIds(strictContentType: false, cancellationToken).StrictlyIncreasing();
    }

    /// <summary>A null receiver or a throwing source fails clearly on every receiver.</summary>
    [Test]
    public async Task NullOrThrowingSource_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string nullBody = null!;
        Stream nullStream = null!;
        System.Net.Http.HttpResponseMessage nullResponse = null!;

        var nullString = await Assert.That(async () => await Assert.That(nullBody).HasSseEventIds()).Throws<AssertionException>();
        await Assert.That(nullString!.Message).Contains("receiver was null");
        await Assert.That(async () => await Assert.That(nullStream).HasSseEventIds(cancellationToken)).Throws<AssertionException>();
        await Assert.That(async () => await Assert.That(nullResponse).HasSseEventIds(cancellationToken)).Throws<AssertionException>();

        Func<Task<string?>> throwingBody = static () => throw new InvalidOperationException("boom");
        Func<Task<Stream?>> throwingStream = static () => throw new InvalidOperationException("boom");
        Func<Task<System.Net.Http.HttpResponseMessage?>> throwingResponse = static () => throw new InvalidOperationException("boom");

        var thrown = await Assert.That(async () => await Assert.That(throwingBody).HasSseEventIds()).Throws<AssertionException>();
        await Assert.That(thrown!.Message).Contains("InvalidOperationException");
        await Assert.That(async () => await Assert.That(throwingStream).HasSseEventIds(cancellationToken)).Throws<AssertionException>();
        await Assert.That(async () => await Assert.That(throwingResponse).HasSseEventIds(cancellationToken)).Throws<AssertionException>();
    }
}
