using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Assertions.Exceptions;

namespace SseAssertions.TUnit.Tests;

/// <summary>
/// Tests for the streaming reads of 0.8.0: every streaming assertion stops once its outcome is decided, so it
/// works on a stream that never ends; <c>Until</c> ends the observation window at a named event; successive
/// assertions on one <see cref="SseStream"/> continue exactly; <c>WithId(predicate)</c>; the positional token on
/// the response receiver; and UTF-8 decoding whatever the charset. The scripted streams are never completed
/// unless a test says so, so a test that passes proves the read stopped by itself.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class EarlyStopAndUntilTests
{
    private static readonly Uri Events = new("/events", UriKind.Relative);

    // --- early stop on a stream that never ends ---

    /// <summary><c>AtLeast(n)</c> passes at the n-th match without waiting for more.</summary>
    [Test]
    public async Task AtLeast_EndlessStream_PassesAtTheNthMatch(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        stream.Send("event: tick\ndata: 1\n\nevent: other\ndata: x\n\nevent: tick\ndata: 2\n\n");

        await Assert.That(stream).HasSseEvent("tick", cancellationToken).AtLeast(2);
    }

    /// <summary><c>AtMost(n)</c> fails as soon as the count is exceeded.</summary>
    [Test]
    public async Task AtMost_EndlessStream_FailsOnceExceeded(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        stream.Send("event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\n");

        var exception = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseEvent("tick", cancellationToken).AtMost(1);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("observed: 2 event(s)");
    }

    /// <summary><c>Exactly(n)</c> fails as soon as the count is exceeded, on the response receiver too.</summary>
    [Test]
    public async Task Exactly_EndlessResponse_FailsOnceExceeded(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        body.Send("data: a\n\ndata: b\n\n");
        using var response = StubHandler.Sse(body);

        await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEvent("message", cancellationToken).Exactly(1);
        }).Throws<AssertionException>();
    }

    /// <summary><c>HasFirstSseEvent</c> reads only the first event.</summary>
    [Test]
    public async Task HasFirstSseEvent_EndlessStream_ReadsOnlyTheFirstEvent(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        stream.Send("event: hello\ndata: 1\n\n");
        using var body = new ScriptedStream();
        body.Send("event: hello\ndata: 1\n\n");
        using var response = StubHandler.Sse(body);

        await Assert.That(stream).HasFirstSseEvent("hello", cancellationToken);
        await Assert.That(response).HasFirstSseEvent("hello", cancellationToken: cancellationToken);
    }

    /// <summary><c>HasSseEventsInOrder</c> passes once the sequence is complete, in order or contiguously.</summary>
    [Test]
    public async Task HasSseEventsInOrder_EndlessStream_PassesOnceComplete(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        stream.Send("event: a\ndata: 1\n\nevent: x\ndata: 1\n\nevent: b\ndata: 1\n\n");
        using var contiguous = new ScriptedStream();
        contiguous.Send("event: a\ndata: 1\n\nevent: a\ndata: 1\n\nevent: b\ndata: 1\n\n");
        using var body = new ScriptedStream();
        body.Send("event: a\ndata: 1\n\nevent: b\ndata: 1\n\n");
        using var response = StubHandler.Sse(body);

        await Assert.That(stream).HasSseEventsInOrder(["a", "b"], cancellationToken: cancellationToken);
        await Assert.That(contiguous).HasSseEventsInOrder(["a", "b"], strictOrdering: true, cancellationToken: cancellationToken);
        await Assert.That(response).HasSseEventsInOrder(["a", "b"], cancellationToken: cancellationToken);
    }

    /// <summary>On the string receiver, <c>AtMost</c> and <c>Exactly</c> fail at the first match beyond the count
    /// and pass when it is not exceeded.</summary>
    [Test]
    public async Task CountTerminators_StringReceiver_DecideLikeAStream(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Body = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\n";

        await Assert.That(async () => await Assert.That(Body).HasSseEvent("tick").AtMost(1)).Throws<AssertionException>();
        await Assert.That(async () => await Assert.That(Body).HasSseEvent("tick").Exactly(1)).Throws<AssertionException>();
        await Assert.That(Body).HasSseEvent("tick").AtMost(2);
        await Assert.That(Body).HasSseEvent("tick").Exactly(2);
        await Assert.That(Body).HasSseEvent("tick").AtLeast(0);
    }

    /// <summary>A throwing source fails the in-order chain on the string receiver.</summary>
    [Test]
    public async Task HasSseEventsInOrder_StringSourceThrows_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Func<Task<string?>> throwingSource = static () => throw new InvalidOperationException("boom");

        var exception = await Assert.That(async () => await Assert.That(throwingSource).HasSseEventsInOrder(["a", "b"]))
            .Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("InvalidOperationException");
    }

    // --- Until ---

    /// <summary><c>Until</c> works on the response receiver too.</summary>
    [Test]
    public async Task Until_ResponseReceiver_EndsTheWindow(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        body.Send("event: tick\ndata: 1\n\nevent: done\ndata: x\n\n");
        using var response = StubHandler.Sse(body);

        await Assert.That(response).HasSseEvent("tick", cancellationToken).Exactly(1).Until("done");
    }

    /// <summary>Every <c>HasSseEventIds</c> chain method works on the stream and response receivers.</summary>
    [Test]
    public async Task EventIdChains_StreamAndResponse_AllMethods(CancellationToken cancellationToken)
    {
        const string Body = "id: 4\ndata: a\n\nid: 5\nevent: done\ndata: b\n\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Body));
        using var otherStream = new MemoryStream(Encoding.UTF8.GetBytes(Body));
        using var response = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes(Body)));
        using var otherResponse = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes(Body)));

        await Assert.That(stream).HasSseEventIds(cancellationToken).Sequential().Until("done");
        await Assert.That(otherStream).HasSseEventIds(cancellationToken).Sequential(startingAt: 4);
        await Assert.That(response).HasSseEventIds(cancellationToken).Sequential().Until("done");
        await Assert.That(otherResponse).HasSseEventIds(cancellationToken).StrictlyIncreasing();
    }

    /// <summary><c>Until</c> ends the window at the named event, so <c>Exactly</c> decides on an endless
    /// stream.</summary>
    [Test]
    public async Task Until_EndsTheWindow_ExactlyDecides(CancellationToken cancellationToken)
    {
        using var stream = new ScriptedStream();
        stream.Send("event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\nevent: done\ndata: x\n\nevent: tick\ndata: 3\n\n");

        await Assert.That(stream).HasSseEvent("tick", cancellationToken).Exactly(2).Until("done");
    }

    /// <summary>Events after the <c>Until</c> event do not count, on the string receiver too.</summary>
    [Test]
    public async Task Until_StringReceiver_IgnoresEventsAfterTheWindow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Body = "event: tick\ndata: 1\n\nevent: done\ndata: x\n\nevent: tick\ndata: 2\n\n";

        await Assert.That(Body).HasSseEvent("tick").Exactly(1).Until("done");
        await Assert.That(Body).HasSseEvent("tick").Exactly(2);
        await Assert.That(Body).HasSseEvent("tick").Exactly(2).Until("never");
    }

    /// <summary>When the token ends the read before the <c>Until</c> event, a met expectation still fails,
    /// naming the missing event.</summary>
    [Test]
    public async Task Until_TokenEndsTheReadFirst_FailsNamingTheEvent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new ScriptedStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseEvent("tick", cts.Token).AtMost(5).Until("done");
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("the read to reach an event of type \"done\"");
        await Assert.That(exception.Message).Contains("canceled after 0 event(s)");
    }

    /// <summary>When the stream ends without the <c>Until</c> event, the whole stream is the window.</summary>
    [Test]
    public async Task Until_StreamEndsFirst_WholeStreamIsTheWindow(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("event: tick\ndata: 1\n\n"));

        await Assert.That(stream).HasSseEvent("tick", cancellationToken).Exactly(1).Until("done");
    }

    /// <summary>An unmet expectation when the token ends the read keeps the cancellation-cut diagnostic, even with
    /// <c>Until</c>.</summary>
    [Test]
    public async Task Until_UnmetWhenTheTokenEndsTheRead_ReportsTheCancellationCut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new ScriptedStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseEvent("tick", cts.Token).AtLeast(1).Until("done");
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("the read was canceled after");
    }

    /// <summary>When the token ends the read before the sequence is complete, the failure is the cancellation cut,
    /// not a missing event.</summary>
    [Test]
    public async Task HasSseEventsInOrder_TokenEndsTheReadBeforeTheSequence_ReportsTheCancellationCut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cts = new CancellationTokenSource();
        using var stream = new CancelAtEndStream(Encoding.UTF8.GetBytes("event: a\ndata: 1\n\n"), cts);

        var exception = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseEventsInOrder(["a", "b"], cancellationToken: cts.Token);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("the read was canceled after 18 byte(s); parsed 1 event(s)");
    }

    // --- successive assertions on one SseStream ---

    /// <summary>Assertions on one <see cref="SseStream"/> continue where the previous one stopped, even when
    /// several frames arrived in one chunk.</summary>
    [Test]
    public async Task SseStream_SuccessiveAssertions_ContinueExactly(CancellationToken cancellationToken)
    {
        using var body = new ScriptedStream();
        using var client = StubHandler.Client(() => StubHandler.Sse(body), out _);
        body.Send("retry: 1000\n\nevent: a\ndata: 1\n\nevent: b\ndata: 2\n\nevent: c\ndata: 3\n\n");

        await using var sse = await SseStream.OpenAsync(client, Events, cancellationToken);

        await Assert.That(sse.FirstFrame.RetryMillis).IsEqualTo(1000);
        await Assert.That(sse).HasSseEvent("a", cancellationToken);
        await Assert.That(sse).HasFirstSseEvent("b", cancellationToken);
        await Assert.That(sse).HasSseEventsInOrder(["c"], cancellationToken: cancellationToken);

        body.Send("event: d\ndata: 4\n\n");
        await Assert.That(sse).HasSseEvent("d", cancellationToken).WithData(static data => string.Equals(data, "4", StringComparison.Ordinal));
    }

    // --- WithId(predicate) ---

    /// <summary><c>WithId(predicate)</c> counts frames whose id satisfies it, on every receiver.</summary>
    [Test]
    public async Task WithIdPredicate_CountsMatchingIds(CancellationToken cancellationToken)
    {
        const string Body = "event: tick\ndata: 1\nid: 7\n\nevent: tick\ndata: 2\n\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Body));
        using var response = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes(Body)));

        await Assert.That(Body).HasSseEvent("tick").WithId(static id => id is not null).Exactly(1);
        await Assert.That(stream).HasSseEvent("tick", cancellationToken).WithId(static id => id is "7");
        await Assert.That(response).HasSseEvent("tick", cancellationToken).WithId(static id => id is null).Exactly(1);
    }

    /// <summary>A predicate no id satisfies fails listing the ids seen; the last <c>WithId</c> wins.</summary>
    [Test]
    public async Task WithIdPredicate_NoMatch_FailsListingIds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Body = "event: tick\ndata: 1\nid: 7\n\nevent: tick\ndata: 2\n\n";

        var exception = await Assert.That(async () =>
        {
            await Assert.That(Body).HasSseEvent("tick").WithId("7").WithId(static id => id is "8");
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("whose id satisfies the predicate");
        await Assert.That(exception.Message).Contains("id: \"7\"");
        await Assert.That(exception.Message).Contains("id: <absent>");

        await Assert.That(Body).HasSseEvent("tick").WithId(static id => id is "8").WithId("7");
    }

    /// <summary>A null predicate or <c>Until</c> name is rejected.</summary>
    [Test]
    public async Task WithIdPredicateAndUntil_NullArguments_Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        using var response = StubHandler.Sse(new MemoryStream());

        await Assert.That(async () => await Assert.That("data: x\n\n").HasSseEvent("message").WithId((Func<string?, bool>)null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That("data: x\n\n").HasSseEvent("message").Until(null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(stream).HasSseEvent("message", cancellationToken).WithId((Func<string?, bool>)null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(stream).HasSseEvent("message", cancellationToken).Until(null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(response).HasSseEvent("message", cancellationToken).WithId((Func<string?, bool>)null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(response).HasSseEvent("message", cancellationToken).Until(null!)).Throws<ArgumentNullException>();
    }

    // --- response receiver: token position and decoding ---

    /// <summary>The response receiver takes the token positionally, by name, or after <c>strictContentType</c>.</summary>
    [Test]
    public async Task ResponseReceiver_TokenBindsInEveryShape(CancellationToken cancellationToken)
    {
        using var positional = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes("data: x\n\n")));
        using var named = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes("data: x\n\n")));
        using var lenient = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes("data: x\n\n")), "text/plain");
        using var lenientPositional = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes("data: x\n\n")), "text/plain");

        await Assert.That(positional).HasSseEvent("message", cancellationToken);
        await Assert.That(named).HasSseEvent("message", cancellationToken: cancellationToken);
        await Assert.That(lenient).HasSseEvent("message", strictContentType: false, cancellationToken: cancellationToken);
        await Assert.That(lenientPositional).HasSseEvent("message", false, cancellationToken);
    }

    /// <summary>The body is decoded as UTF-8 whatever charset the response declares, as the SSE specification
    /// requires.</summary>
    [Test]
    public async Task ResponseReceiver_DecodesUtf8WhateverTheCharset(CancellationToken cancellationToken)
    {
        using var response = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes("data: café\n\n")));
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/event-stream; charset=iso-8859-1");

        await Assert.That(response).HasSseEvent("message", cancellationToken).WithData(static data => string.Equals(data, "café", StringComparison.Ordinal));
    }

    /// <summary>A body that delivers its bytes and then, on the read that would end it, cancels
    /// <paramref name="source"/>: a token that fires while the reader waits for more data.</summary>
    /// <param name="bytes">The bytes delivered before the cancellation.</param>
    /// <param name="source">The token source canceled at the end of the bytes.</param>
    private sealed class CancelAtEndStream(byte[] bytes, CancellationTokenSource source) : MemoryStream(bytes)
    {
        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                return read;
            }

            await source.CancelAsync();
            throw new OperationCanceledException(source.Token);
        }
    }
}
