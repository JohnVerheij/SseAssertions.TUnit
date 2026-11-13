using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions.Exceptions;

namespace SseAssertions.TUnit.Tests;

/// <summary>
/// Real-use tests for an open <see cref="SseStream"/>: the stream-level <c>HasSseContentType</c>, two subscribers
/// on one endpoint, early ends, cancellation between and inside frames, and a slow or huge producer, all through
/// an in-memory HTTP handler.
/// </summary>
[Category("Smoke")]
[Timeout(20_000)]
internal sealed class SseStreamUseTests
{
    private static readonly Uri Events = new("/events", UriKind.Relative);

    /// <summary>Opens a stream over a scripted body whose first frame is <paramref name="first"/>.</summary>
    private static async Task<(SseStream Stream, ScriptedStream Body)> OpenAsync(string first, string? mediaType, CancellationToken ct)
    {
        var body = new ScriptedStream();
        body.Send(first);
        using var client = StubHandler.Client(() => ResponseOver(body, mediaType), out _);
        var stream = await SseStream.OpenAsync(client, Events, ct);
        return (stream, body);
    }

    private static HttpResponseMessage ResponseOver(Stream body, string? mediaType)
    {
        var content = new StreamContent(body);
        if (mediaType is not null)
        {
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    [Test]
    public async Task HasSseContentType_OpenStream_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (sse, _) = await OpenAsync("retry: 1000\n\n", "text/event-stream", ct);
        await using var stream = sse;

        await Assert.That(stream).HasSseContentType();
        await Assert.That(stream).HasSseContentType(strict: true);
    }

    [Test]
    public async Task HasSseContentType_CharsetParameter_PassesNonStrictFailsStrict(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (sse, _) = await OpenAsync("retry: 1000\n\n", "text/event-stream; charset=utf-8", ct);
        await using var stream = sse;

        await Assert.That(stream).HasSseContentType();
        var exception = await Assert.That(async () => await Assert.That(stream).HasSseContentType(strict: true)).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains(
            "Expected to have Content-Type 'text/event-stream' with strict set to True\nbut the Content-Type was \"text/event-stream; charset=utf-8\"");
    }

    [Test]
    public async Task HasSseContentType_DoesNotConsumeTheStream(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (sse, body) = await OpenAsync("retry: 1000\n\n", "text/event-stream", ct);
        await using var stream = sse;
        body.Send("event: tick\ndata: 1\n\n");

        await Assert.That(stream).HasSseContentType();
        await Assert.That(stream).HasSseEvent("tick", ct).Exactly(1).Until("tick");
    }

    [Test]
    public async Task HasSseContentType_NullStream_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => SseStreamAndHttpAssertions.HasSseContentType((SseStream)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TwoSubscribers_AreAssertedIndependently(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bodies = new List<ScriptedStream>();
        using var client = StubHandler.Client(() =>
        {
            var body = new ScriptedStream();
            body.Send("retry: 1000\n\n");
            bodies.Add(body);
            return StubHandler.Sse(body);
        }, out _);

        await using var first = await SseStream.OpenAsync(client, Events, ct);
        await using var second = await SseStream.OpenAsync(client, Events, ct);

        // Trigger once: the endpoint publishes to every subscriber.
        foreach (var body in bodies)
        {
            body.Send("id: 1\nevent: tick\ndata: a\n\nid: 2\nevent: tick\ndata: b\n\n");
        }

        await Task.WhenAll(
            Check(async () => await Assert.That(first).HasSseEventIds(ct).Sequential(startingAt: 1).Until("tick")),
            Check(async () => await Assert.That(second).HasSseEvent("tick", ct).WithData(static d => d is "a").Exactly(1).Until("tick")));

        // Each stream continues after what its own assertion read.
        await Assert.That(first).HasSseEvent("tick", ct).WithData(static d => d is "b").Exactly(1).Until("tick");
        await Assert.That(second).HasSseEvent("tick", ct).WithData(static d => d is "b").Exactly(1).Until("tick");
    }

    private static async Task Check(Func<Task> assertion) => await assertion();

    [Test]
    public async Task EndsAfterTheFirstFrame_AssertionFailsInsteadOfHanging(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (sse, body) = await OpenAsync("retry: 1000\n\n", "text/event-stream", ct);
        await using var stream = sse;
        body.Complete();

        var exception = await Assert.That(async () => await Assert.That(stream).HasSseEvent("tick", ct)).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("but observed 0 events of type \"tick\" out of 0 total event(s) captured");
    }

    [Test]
    public async Task EndsMidFrame_TheUnterminatedFrameStillCounts(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: tick\ndata: 1";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        using var response = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes(body)));

        await Assert.That(body).HasSseEvent("tick").Exactly(1);
        await Assert.That(stream).HasSseEvent("tick", ct).Exactly(1);
        await Assert.That(response).HasSseEvent("tick", cancellationToken: ct).Exactly(1);
    }

    [Test]
    public async Task CanceledMidFrame_ReportsTheCutAndTheStreamStaysUsable(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (sse, body) = await OpenAsync("retry: 1000\n\n", "text/event-stream", ct);
        await using var stream = sse;
        body.Send("event: tick\nda");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var exception = await Assert.That(async () => await Assert.That(stream).HasSseEvent("tick", cts.Token)).Throws<AssertionException>();

        var message = exception!.Message.ReplaceLineEndings("\n");
        await Assert.That(message[..message.IndexOf("\n\nat Assert.That", StringComparison.Ordinal)]).IsEqualTo(
            "Expected to find at least 1 event(s) of type \"tick\"\n"
            + "but the read was canceled after 14 byte(s); parsed 0 event(s) from the partial buffer\n"
            + "  partial body excerpt: event: tick\n");

        // The rest of the frame arrives later: a new assertion with a new token sees the whole frame.
        body.Send("ta: 1\n\n");
        await Assert.That(stream).HasSseEvent("tick", ct).WithData(static d => d is "1").Exactly(1).Until("tick");
    }

    [Test]
    public async Task CanceledBetweenFrames_TheNextAssertionContinues(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (sse, body) = await OpenAsync("retry: 1000\n\n", "text/event-stream", ct);
        await using var stream = sse;
        body.Send("event: a\ndata: 1\n\n");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var exception = await Assert.That(async () => await Assert.That(stream).HasSseEvent("b", cts.Token)).Throws<AssertionException>();
        body.Send("event: b\ndata: 2\n\n");

        await Assert.That(exception!.Message).Contains("canceled after");
        await Assert.That(stream).HasSseEvent("b", ct).Exactly(1).Until("b");
    }

    [Test]
    public async Task FirstFrame_BareRetry_ExposesTheDirective(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (sse, _) = await OpenAsync("retry: 3000\n\n", "text/event-stream", ct);
        await using var stream = sse;

        await Assert.That(stream.FirstFrame.RetryMillis).IsEqualTo(3000);
        await Assert.That(stream.FirstFrame.Data).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task OneByteAtATime_ProducerIsReadLikeAnyOther(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (sse, body) = await OpenAsync("retry: 1000\n\n", "text/event-stream", ct);
        await using var stream = sse;
        var assertion = Check(async () => await Assert.That(stream).HasSseEvent("tick", ct).WithData(static d => d is "café").Exactly(1).Until("tick"));

        foreach (var b in Encoding.UTF8.GetBytes("event: tick\r\ndata: café\r\n\r\n"))
        {
            body.Send([b]);
            await Task.Yield();
        }

        await assertion;
    }

    [Test]
    public async Task DisposedWhileAReadIsPending_TheReadEnds(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (stream, _) = await OpenAsync("retry: 1000\n\n", "text/event-stream", ct);
        var pending = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in stream.ReadEventsAsync(ct))
                {
                    _ = evt;
                }

                return "ended";
            }
            catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException or IOException)
            {
                return e.GetType().Name;
            }
        }, ct);
        await Task.Delay(50, ct);

        await stream.DisposeAsync();

        await Assert.That(await pending).IsNotNull();
    }

    [Test]
    public async Task HundredThousandEvents_AreCountedWithoutBuffering(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const int Count = 100_000;
        var sb = new StringBuilder("retry: 1000\n\n");
        for (var i = 0; i < Count; i++)
        {
            sb.Append("event: tick\ndata: ").Append(i).Append("\n\n");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

        await Assert.That(stream).HasSseEvent("tick", ct).Exactly(Count);
    }

    [Test]
    public async Task VeryLargeDataField_IsReadWhole(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var payload = new string('x', 2_000_000);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("event: big\ndata: " + payload + "\n\n"));

        await Assert.That(stream).HasSseEvent("big", ct).WithData(d => d.Length == payload.Length).Exactly(1);
    }

    [Test]
    public async Task ResponseRead_Twice_SecondReadSeesTheEndOfTheBody(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = StubHandler.Sse(new MemoryStream(Encoding.UTF8.GetBytes("event: tick\ndata: 1\n\n")));

        await Assert.That(response).HasSseEvent("tick", cancellationToken: ct).Exactly(1);
        var exception = await Assert.That(async () => await Assert.That(response).HasSseEvent("tick", cancellationToken: ct)).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("but observed 0 events of type \"tick\" out of 0 total event(s) captured");
    }

    [Test]
    public async Task EmptyEventName_MatchesNothingButAFrameNamedNothing(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var exception = await Assert.That(async () => await Assert.That("data: x\n\n").HasSseEvent(string.Empty)).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("Expected to find at least 1 event(s) of type \"\"");
    }

    [Test]
    public async Task NullReceivers_FailWithTheReceiverWasNull(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var exception = await Assert.That(async () => await Assert.That((string?)null).HasSseEvent("tick")).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("the receiver was null");
    }

    [Test]
    public async Task HttpStatusCodeError_OpenAsyncThrowsBeforeAnyAssertion(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var client = StubHandler.Client(() => StubHandler.Sse(new MemoryStream(), status: HttpStatusCode.InternalServerError), out _);

        await Assert.That(async () => await SseStream.OpenAsync(client, Events, ct)).Throws<HttpRequestException>();
    }

    [Test]
    public async Task HasSseEventsInOrder_NoNames_PassesWithoutReading(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var body = new ScriptedStream();
        using var response = StubHandler.Sse(new ScriptedStream());

        await Assert.That(body).HasSseEventsInOrder([], cancellationToken: ct);
        await Assert.That(response).HasSseEventsInOrder([], cancellationToken: ct);
    }

    /// <summary>When the token ends the read and the expectation is already met, the assertion passes on what
    /// arrived: only an unmet expectation reports the cancellation cut.</summary>
    [Test]
    public async Task TokenEndsTheRead_ExpectationAlreadyMet_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var body = new ScriptedStream();
        body.Send("event: tick\ndata: 1\n\n");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.That(body).HasSseEvent("tick", cts.Token).AtMost(5);
    }

    /// <summary>The retry-directive assertions read to the end, or until the token ends the read; what arrived
    /// is evaluated.</summary>
    [Test]
    public async Task RetryDirective_TokenEndsTheRead_TheArrivedBodyIsEvaluated(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var body = new ScriptedStream();
        body.Send("retry: 3000\n\n");
        using var responseBody = new ScriptedStream();
        responseBody.Send("retry: 3000\n\n");
        using var response = StubHandler.Sse(responseBody);
        using var streamCts = new CancellationTokenSource();
        streamCts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.That(body).HasSseRetryDirective(3000, streamCts.Token);

        using var responseCts = new CancellationTokenSource();
        responseCts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await Assert.That(response).HasSseRetryDirective(3000, cancellationToken: responseCts.Token);
    }

    /// <summary>The event enumeration takes the token of the call and the token of <c>WithCancellation</c>
    /// together.</summary>
    [Test]
    public async Task ReadEventsAsync_TokenOfTheCallAndOfWithCancellation_BothApply(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (sse, body) = await OpenAsync("retry: 1000\n\n", "text/event-stream", ct);
        await using var stream = sse;
        body.Send("event: tick\ndata: 1\n\n");
        using var other = new CancellationTokenSource();
        var seen = new List<string>();

        var enumerator = stream.ReadEventsAsync(ct).WithCancellation(other.Token).GetAsyncEnumerator();
        await using (enumerator)
        {
            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            seen.Add(enumerator.Current.Data);
        }

        await Assert.That(seen).IsEquivalentTo(["1"]);

        await other.CancelAsync();
        await Assert.That(async () =>
        {
            await foreach (var evt in stream.ReadEventsAsync(ct).WithCancellation(other.Token))
            {
                _ = evt;
            }
        }).Throws<OperationCanceledException>();
    }
}
