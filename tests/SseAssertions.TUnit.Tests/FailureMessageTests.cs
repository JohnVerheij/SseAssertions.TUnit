using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions.Exceptions;

namespace SseAssertions.TUnit.Tests;

/// <summary>
/// Pins the full text of the failures of the flat assertions and of the chains' common cases: the expectation line (from <c>ExpectationMessage</c> or
/// <c>GetExpectation()</c>) and the <c>but</c> line (what was found). The trailing <c>at Assert.That(...)</c>
/// expression line is TUnit's own rendering and is left out of the pins; it is checked once, for the
/// double-expression bug, in <see cref="ChainExpression_NamesTheEntryPointOnce"/>.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class FailureMessageTests
{
    private const string Tick = "event: tick\ndata: 1\n\n";

    /// <summary>Runs <paramref name="assertion"/>, which must fail, and returns the message without TUnit's
    /// trailing expression line.</summary>
    private static async Task<string> FailureOf(Func<Task> assertion)
    {
        var exception = await Assert.That(assertion).Throws<AssertionException>();
        var message = exception!.Message.ReplaceLineEndings("\n");
        var cut = message.IndexOf("\n\nat Assert.That", StringComparison.Ordinal);
        return cut < 0 ? message : message[..cut];
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static HttpResponseMessage Response(string body, string? contentType = "text/event-stream")
    {
        var content = new StreamContent(Bytes(body));
        if (contentType is not null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static string Lines(params string[] lines) => string.Join('\n', lines);

    [Test]
    public async Task IsServerSentEventsStream_NoMarker(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "hello";

        var message = await FailureOf(async () => await Assert.That(body).IsServerSentEventsStream());

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have the shape of a Server-Sent Events stream",
            "but no SSE field marker (event:, data:, id:, retry:) was found before a frame separator (\\n\\n)"));
    }

    [Test]
    public async Task HasFirstSseEvent_String_WrongName(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var message = await FailureOf(async () => await Assert.That(Tick).HasFirstSseEvent("other"));

        await Assert.That(message).IsEqualTo(Lines("Expected to have the first event named 'other'", "but the first event was \"tick\""));
    }

    [Test]
    public async Task HasFirstSseEvent_String_NoEvents(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var message = await FailureOf(async () => await Assert.That(string.Empty).HasFirstSseEvent("other"));

        await Assert.That(message).IsEqualTo(Lines("Expected to have the first event named 'other'", "but the stream contained no events"));
    }

    [Test]
    public async Task HasFirstSseEvent_Stream_WrongName(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Bytes(Tick);

        var message = await FailureOf(async () => await Assert.That(stream).HasFirstSseEvent("other", ct));

        await Assert.That(message).IsEqualTo(Lines("Expected to have the first event named 'other'", "but the first event was \"tick\""));
    }

    [Test]
    public async Task HasFirstSseEvent_Stream_NoEvents(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Bytes(string.Empty);

        var message = await FailureOf(async () => await Assert.That(stream).HasFirstSseEvent("other", ct));

        await Assert.That(message).IsEqualTo(Lines("Expected to have the first event named 'other'", "but the stream contained no events"));
    }

    [Test]
    public async Task HasFirstSseEvent_Response_WrongName(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick);

        var message = await FailureOf(async () => await Assert.That(response).HasFirstSseEvent("other", cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines("Expected to have the first event named 'other'", "but the first event was \"tick\""));
    }

    [Test]
    public async Task HasFirstSseEvent_Response_WrongContentType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick, "text/plain");

        var message = await FailureOf(async () => await Assert.That(response).HasFirstSseEvent("tick", cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have the first event named 'tick'",
            "but the response Content-Type was \"text/plain\" (expected \"text/event-stream\")"));
    }

    [Test]
    public async Task HasSseContentType_Absent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick, null);

        var message = await FailureOf(async () => await Assert.That(response).HasSseContentType());

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have Content-Type 'text/event-stream' with strict set to False",
            "but the response had no Content-Type header"));
    }

    [Test]
    public async Task HasSseContentType_OtherMediaType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick, "application/json");

        var message = await FailureOf(async () => await Assert.That(response).HasSseContentType());

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have Content-Type 'text/event-stream' with strict set to False",
            "but the Content-Type was \"application/json\""));
    }

    [Test]
    public async Task HasSseContentType_StrictWithParameter(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick, "text/event-stream; charset=utf-8");

        var message = await FailureOf(async () => await Assert.That(response).HasSseContentType(strict: true));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have Content-Type 'text/event-stream' with strict set to True",
            "but the Content-Type was \"text/event-stream; charset=utf-8\", which has parameters that strict mode does not allow"));
    }

    [Test]
    public async Task HasSseEventsInOrder_Stream_Missing(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Bytes(Tick);

        var message = await FailureOf(async () => await Assert.That(stream).HasSseEventsInOrder(["tick", "b"], cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to contain the named events in order (strict ordering: False)",
            "but \"b\" was not in the stream"));
    }

    [Test]
    public async Task HasSseEventsInOrder_Stream_StrictMismatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Bytes(Tick + "event: z\ndata: 1\n\nevent: b\ndata: 1\n\n");

        var message = await FailureOf(async () => await Assert.That(stream).HasSseEventsInOrder(["tick", "b"], strictOrdering: true, cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to contain the named events in order (strict ordering: True)",
            "but \"z\" appeared at index 1 instead of \"b\""));
    }

    [Test]
    public async Task HasSseEventsInOrder_Response_WrongContentType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick, "text/plain");

        var message = await FailureOf(async () => await Assert.That(response).HasSseEventsInOrder(["tick"], cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to contain the named events in order (strict ordering: False)",
            "but the response Content-Type was \"text/plain\" (expected \"text/event-stream\")"));
    }

    [Test]
    public async Task HasSseEventsInOrder_Response_Missing(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick);

        var message = await FailureOf(async () => await Assert.That(response).HasSseEventsInOrder(["tick", "b"], cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to contain the named events in order (strict ordering: False)",
            "but \"b\" was not in the stream"));
    }

    [Test]
    public async Task HasSseEventsInOrder_String_Missing(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var message = await FailureOf(async () => await Assert.That(Tick).HasSseEventsInOrder(["tick", "b"]));

        await Assert.That(message).IsEqualTo(Lines("Expected to contain events [tick, b] in order", "but \"b\" was not in the stream"));
    }

    [Test]
    public async Task HasSseEventsInOrder_String_OutOfOrder(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: b\ndata: 1\n\nevent: a\ndata: 1\n\n";

        var message = await FailureOf(async () => await Assert.That(body).HasSseEventsInOrder(["a", "b"]));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to contain events [a, b] in order",
            "but \"b\" (index 0) appeared before \"a\" (index 1)"));
    }

    [Test]
    public async Task HasSseEventsInOrder_String_StrictTooFewEvents(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var message = await FailureOf(async () => await Assert.That(Tick).HasSseEventsInOrder(["tick", "b"]).WithStrictOrdering());

        await Assert.That(message).IsEqualTo(Lines("Expected to contain events [tick, b] contiguously", "but the stream had only 1 event(s)"));
    }

    [Test]
    public async Task HasSseEventsInOrder_String_StrictPartialPrefix(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: x\ndata: 1\n\nevent: a\ndata: 1\n\nevent: b\ndata: 1\n\n";

        var message = await FailureOf(async () => await Assert.That(body).HasSseEventsInOrder(["a", "b", "c"]).WithStrictOrdering());

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to contain events [a, b, c] contiguously",
            "but the stream ended after matching prefix [a, b] starting at index 1"));
    }

    [Test]
    public async Task HasSseEventsInOrder_String_StrictFirstNameAbsent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var message = await FailureOf(async () => await Assert.That("event: x\ndata: 1\n\nevent: y\ndata: 1\n\n").HasSseEventsInOrder(["a", "b"]).WithStrictOrdering());

        await Assert.That(message).IsEqualTo(Lines("Expected to contain events [a, b] contiguously", "but \"a\" was not in the stream"));
    }

    [Test]
    public async Task HasSseRetryDirective_NoRetry_AnyValue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var message = await FailureOf(async () => await Assert.That(Tick).HasSseRetryDirective());

        await Assert.That(message).IsEqualTo(Lines("Expected to contain a retry directive", "but no retry directive was found"));
    }

    [Test]
    public async Task HasSseRetryDirective_String_OtherValues(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "retry: 3\ndata: x\n\nretry: 4\ndata: y\n\n";

        var message = await FailureOf(async () => await Assert.That(body).HasSseRetryDirective(5));

        await Assert.That(message).IsEqualTo(Lines("Expected to contain a retry directive", "but observed retry value(s): 3, 4, none equal to 5"));
    }

    [Test]
    public async Task HasSseRetryDirective_String_NoRetry_ExactValue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var message = await FailureOf(async () => await Assert.That(Tick).HasSseRetryDirective(5));

        await Assert.That(message).IsEqualTo(Lines("Expected to contain a retry directive", "but no retry directive was found"));
    }

    [Test]
    public async Task HasSseRetryDirective_Stream_OtherValue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Bytes("retry: 3\n\n");

        var message = await FailureOf(async () => await Assert.That(stream).HasSseRetryDirective(5, ct));

        await Assert.That(message).IsEqualTo(Lines("Expected to contain a retry directive", "but observed retry value(s): 3, none equal to 5"));
    }

    [Test]
    public async Task HasSseRetryDirective_Response_NoRetry(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick);

        var message = await FailureOf(async () => await Assert.That(response).HasSseRetryDirective(cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines("Expected to contain a retry directive", "but no retry directive was found"));
    }

    [Test]
    public async Task HasSseRetryDirective_Response_WrongContentType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick, "text/plain");

        var message = await FailureOf(async () => await Assert.That(response).HasSseRetryDirective(cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to contain a retry directive",
            "but the response Content-Type was \"text/plain\" (expected \"text/event-stream\")"));
    }

    [Test]
    public async Task HasSseRetryDirectiveFirst_String_None(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var message = await FailureOf(async () => await Assert.That(Tick).HasSseRetryDirectiveFirst());

        await Assert.That(message).IsEqualTo(Lines("Expected to send a retry directive before any data", "but no retry directive was found"));
    }

    [Test]
    public async Task HasSseRetryDirectiveFirst_String_DataFirst(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "data: x\n\nretry: 3\n\n";

        var message = await FailureOf(async () => await Assert.That(body).HasSseRetryDirectiveFirst());

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to send a retry directive before any data",
            "but a data field appeared before the first retry directive"));
    }

    [Test]
    public async Task HasSseRetryDirectiveFirst_String_WrongValue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "retry: 3\ndata: x\n\n";

        var message = await FailureOf(async () => await Assert.That(body).HasSseRetryDirectiveFirst(5));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to send a retry directive of 5 ms before any data",
            "but the leading retry directive was \"retry: 3\""));
    }

    [Test]
    public async Task HasSseRetryDirectiveFirst_Stream_None(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Bytes(Tick);

        var message = await FailureOf(async () => await Assert.That(stream).HasSseRetryDirectiveFirst(ct));

        await Assert.That(message).IsEqualTo(Lines("Expected to send a retry directive before any data", "but no retry directive was found"));
    }

    [Test]
    public async Task HasSseRetryDirectiveFirst_Stream_WrongValue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Bytes("retry: 3\n\n");

        var message = await FailureOf(async () => await Assert.That(stream).HasSseRetryDirectiveFirst(5, ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to send a retry directive of 5 ms before any data",
            "but the leading retry directive was \"retry: 3\""));
    }

    [Test]
    public async Task HasSseRetryDirectiveFirst_Response_None(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick);

        var message = await FailureOf(async () => await Assert.That(response).HasSseRetryDirectiveFirst(cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines("Expected to send a retry directive before any data", "but no retry directive was found"));
    }

    [Test]
    public async Task HasSseRetryDirectiveFirst_Response_WrongValue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response("retry: 3\n\n");

        var message = await FailureOf(async () => await Assert.That(response).HasSseRetryDirectiveFirst(5, cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to send a retry directive of 5 ms before any data",
            "but the leading retry directive was \"retry: 3\""));
    }

    [Test]
    public async Task HasSseRetryDirectiveFirst_Response_WrongContentType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick, null);

        var message = await FailureOf(async () => await Assert.That(response).HasSseRetryDirectiveFirst(5, cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to send a retry directive of 5 ms before any data",
            "but the response had no Content-Type header (expected \"text/event-stream\")"));
    }

    [Test]
    public async Task EndsCleanlyOnCancellation_Stream_TransportFailure(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new FaultingStream(new IOException("boom"));

        var message = await FailureOf(async () => await Assert.That(stream).EndsCleanlyOnCancellation(ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to end cleanly when its read is canceled",
            "but the read threw IOException instead of ending with cooperative cancellation: boom"));
    }

    [Test]
    public async Task EndsCleanlyOnCancellation_Stream_ForeignCancellation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new FaultingStream(new OperationCanceledException("foreign"));

        var message = await FailureOf(async () => await Assert.That(stream).EndsCleanlyOnCancellation(ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to end cleanly when its read is canceled",
            "but the read was canceled by a different token than the supplied one (for example, an HttpClient timeout): OperationCanceledException: foreign"));
    }

    [Test]
    public async Task EndsCleanlyOnCancellation_Response_WrongContentType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = Response(Tick, "text/plain");

        var message = await FailureOf(async () => await Assert.That(response).EndsCleanlyOnCancellation(cancellationToken: ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to end cleanly when its read is canceled",
            "but the response Content-Type was \"text/plain\" (expected \"text/event-stream\")"));
    }

    [Test]
    public async Task HasSseEvent_Missing_ListsTheCapturedEvents(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Bytes(Tick);

        var message = await FailureOf(async () => await Assert.That(stream).HasSseEvent("zzz", ct));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to find at least 1 event(s) of type \"zzz\"",
            "but observed 0 events of type \"zzz\" out of 1 total event(s) captured:",
            "    [0] event=tick data=\"1\""));
    }

    [Test]
    public async Task HasSseEvent_CountTerminators_NameTheComparison(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\n";

        var atLeast = await FailureOf(async () => await Assert.That(body).HasSseEvent("tick").AtLeast(3));
        var atMost = await FailureOf(async () => await Assert.That(body).HasSseEvent("tick").AtMost(1));
        var exactly = await FailureOf(async () => await Assert.That(body).HasSseEvent("tick").Exactly(3));

        await Assert.That(atLeast).IsEqualTo(Lines("Expected to find at least 3 event(s) of type \"tick\"", "but observed 2 event(s) of type \"tick\""));
        await Assert.That(atMost).IsEqualTo(Lines("Expected to find at most 1 event(s) of type \"tick\"", "but observed 2 event(s) of type \"tick\""));
        await Assert.That(exactly).IsEqualTo(Lines("Expected to find exactly 3 event(s) of type \"tick\"", "but observed 2 event(s) of type \"tick\""));
    }

    [Test]
    public async Task HasSseEvent_Narrowers_AreNamedInTheExpectation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: tick\ndata: 1\nid: a\nretry: 7\n\n";

        var data = await FailureOf(async () => await Assert.That(body).HasSseEvent("tick").WithData(static d => d is "2"));
        var parsed = await FailureOf(async () => await Assert.That(body).HasSseEvent("tick").WithDataParsedAs(static d => d, static d => d is "2"));
        var id = await FailureOf(async () => await Assert.That(body).HasSseEvent("tick").WithId("zzz"));
        var idPredicate = await FailureOf(async () => await Assert.That(body).HasSseEvent("tick").WithId(static i => i is "zzz"));
        var retry = await FailureOf(async () => await Assert.That(body).HasSseEvent("tick").WithRetryMillis(static r => r is 8));
        var until = await FailureOf(async () => await Assert.That(body).HasSseEvent("tick").AtLeast(2).Until("done"));

        await Assert.That(data).IsEqualTo(Lines(
            "Expected to find at least 1 event(s) of type \"tick\" whose Data satisfies the predicate",
            "but observed 1 event(s) of type \"tick\", none whose Data satisfied the predicate:",
            "    data: \"1\""));
        await Assert.That(parsed).IsEqualTo(data);
        await Assert.That(id).IsEqualTo(Lines(
            "Expected to find at least 1 event(s) of type \"tick\" with id \"zzz\"",
            "but observed 1 event(s) of type \"tick\", none with id \"zzz\":",
            "    id: \"a\""));
        await Assert.That(idPredicate).IsEqualTo(Lines(
            "Expected to find at least 1 event(s) of type \"tick\" whose id satisfies the predicate",
            "but observed 1 event(s) of type \"tick\", none whose id satisfied the predicate:",
            "    id: \"a\""));
        await Assert.That(retry).IsEqualTo(Lines(
            "Expected to find at least 1 event(s) of type \"tick\" whose RetryMillis satisfies the predicate",
            "but observed 1 event(s) of type \"tick\", none whose RetryMillis satisfied the predicate:",
            "    retry: 7"));
        await Assert.That(until).IsEqualTo(Lines(
            "Expected to find at least 2 event(s) of type \"tick\", observed until an event of type \"done\"",
            "but observed 1 event(s) of type \"tick\""));
    }

    [Test]
    public async Task HasSseEvent_ParserThrows_NamesTheDeserializerFailure(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: order\ndata: {bad\n\n";

        var message = await FailureOf(async () => await Assert.That(body).HasSseEvent("order")
            .WithDataParsedAs<int>(static d => throw new InvalidOperationException("malformed"), static v => v > 0));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to find at least 1 event(s) of type \"order\" whose Data satisfies the predicate",
            "but the deserializer threw InvalidOperationException: malformed",
            "  data: \"{bad\""));
    }

    [Test]
    public async Task HasSseEventIds_Violations(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string noId = "event: tick\ndata: 1\n\n";
        const string repeated = "id: 1\ndata: a\n\nid: 1\ndata: b\n\n";
        const string gap = "id: 1\ndata: a\n\nid: 3\ndata: b\n\n";
        const string text = "id: x\ndata: a\n\n";

        var missing = await FailureOf(async () => await Assert.That(noId).HasSseEventIds());
        var notIncreasing = await FailureOf(async () => await Assert.That(repeated).HasSseEventIds().StrictlyIncreasing());
        var notSequential = await FailureOf(async () => await Assert.That(gap).HasSseEventIds().Sequential());
        var startsElsewhere = await FailureOf(async () => await Assert.That(gap).HasSseEventIds().Sequential(5));
        var notInteger = await FailureOf(async () => await Assert.That(text).HasSseEventIds().Sequential());
        var empty = await FailureOf(async () => await Assert.That(string.Empty).HasSseEventIds());

        await Assert.That(missing).IsEqualTo(Lines("Expected every event to carry an id", "but event 1 (tick) carries no id", "  ids: <absent>"));
        await Assert.That(notIncreasing).IsEqualTo(Lines(
            "Expected every event to carry an integer id, increasing strictly",
            "but event 2 has the id 1, not greater than the previous id 1",
            "  ids: \"1\", \"1\""));
        await Assert.That(notSequential).IsEqualTo(Lines(
            "Expected every event to carry an integer id, sequential",
            "but event 2 has the id 3 where 2 was expected",
            "  ids: \"1\", \"3\""));
        await Assert.That(startsElsewhere).IsEqualTo(Lines(
            "Expected every event to carry an integer id, sequential from 5",
            "but event 1 has the id 1 where 5 was expected",
            "  ids: \"1\""));
        await Assert.That(notInteger).IsEqualTo(Lines(
            "Expected every event to carry an integer id, sequential",
            "but event 1 has the id \"x\", which is not an integer",
            "  ids: \"x\""));
        await Assert.That(empty).IsEqualTo(Lines("Expected every event to carry an id", "but no event arrived"));
    }

    /// <summary>A chain's failure names the entry point once. The chain classes used to append it themselves
    /// while the generated extension appended it too, which printed <c>.HasSseEvent("x").HasSseEvent("x")</c>.</summary>
    [Test]
    public async Task ChainExpression_NamesTheEntryPointOnce(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = Bytes(Tick);
        using var response = Response(Tick);
        const string body = Tick;

        var events = await Assert.That(async () => await Assert.That(body).HasSseEvent("zzz")).Throws<AssertionException>();
        var streamEvents = await Assert.That(async () => await Assert.That(stream).HasSseEvent("zzz", ct)).Throws<AssertionException>();
        var responseEvents = await Assert.That(async () => await Assert.That(response).HasSseEvent("zzz", cancellationToken: ct)).Throws<AssertionException>();
        var ids = await Assert.That(async () => await Assert.That(body).HasSseEventIds()).Throws<AssertionException>();
        var order = await Assert.That(async () => await Assert.That(body).HasSseEventsInOrder(["zzz"])).Throws<AssertionException>();

        await Assert.That(Count(events!.Message, ".HasSseEvent(\"zzz\"")).IsEqualTo(1);
        await Assert.That(Count(streamEvents!.Message, ".HasSseEvent(")).IsEqualTo(1);
        await Assert.That(Count(responseEvents!.Message, ".HasSseEvent(")).IsEqualTo(1);
        await Assert.That(Count(ids!.Message, ".HasSseEventIds(")).IsEqualTo(1);
        await Assert.That(Count(order!.Message, ".HasSseEventsInOrder(")).IsEqualTo(1);
    }

    private static int Count(string text, string value) => text.Split(value).Length - 1;

    /// <summary>A stream whose reads throw the given exception.</summary>
    private sealed class FaultingStream(Exception exception) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            // Read-only.
        }

        public override int Read(byte[] buffer, int offset, int count) => throw exception;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw exception;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
