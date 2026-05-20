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
/// End-to-end tests for the <c>HasFirstSseEvent(eventName)</c> assertion across all three
/// receivers (<see cref="string"/>, <see cref="Stream"/>, <see cref="HttpResponseMessage"/>).
/// Covers the happy path, "first event different" failure, empty-stream failure, the WHATWG
/// default-event-name rule (unlabelled frame matches <c>"message"</c>), and argument validation.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class HasFirstSseEventTests
{
    private const string CycleThenComplete = "event: cycleStarted\ndata: 1\n\nevent: cycleCompleted\ndata: 2\n\n";

    [Test]
    public async Task String_FirstEventMatches_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(CycleThenComplete).HasFirstSseEvent("cycleStarted");
    }

    [Test]
    public async Task String_FirstEventDifferent_FailsWithObservedFirstEvent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(CycleThenComplete).HasFirstSseEvent("cycleCompleted");
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"cycleCompleted\"");
        await Assert.That(ex.Message).Contains("\"cycleStarted\"");
    }

    [Test]
    public async Task String_EmptyBody_FailsWithNoEventsMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(string.Empty).HasFirstSseEvent("anything");
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("no events");
    }

    [Test]
    public async Task String_UnlabelledFrameMatchesMessageDefault_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // WHATWG: a frame with no `event:` directive defaults to event name "message".
        const string body = "data: hello\n\n";
        await Assert.That(body).HasFirstSseEvent("message");
    }

    [Test]
    public async Task String_NullBody_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string nullBody = null!;
        var ex = await Assert.That(async () => await Task.Run(() => nullBody.HasFirstSseEvent("x")))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task String_NullEventName_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string nullName = null!;
        var ex = await Assert.That(async () => await Task.Run(() => CycleThenComplete.HasFirstSseEvent(nullName)))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task Stream_FirstEventMatches_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(CycleThenComplete);
        await Assert.That(stream).HasFirstSseEvent("cycleStarted", cancellationToken: ct);
    }

    [Test]
    public async Task Stream_FirstEventDifferent_FailsWithObservedFirstEvent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(CycleThenComplete);
        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).HasFirstSseEvent("cycleCompleted", cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"cycleStarted\"");
    }

    [Test]
    public async Task Stream_EmptyStream_FailsWithNoEventsMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(string.Empty);
        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).HasFirstSseEvent("anything", cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("no events");
    }

    [Test]
    public async Task Stream_NullStream_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream nullStream = null!;
        var ex = await Assert.That(async () => await nullStream.HasFirstSseEvent("x", cancellationToken: ct))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task Http_FirstEventMatches_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(CycleThenComplete, "text/event-stream");
        await Assert.That(response).HasFirstSseEvent("cycleStarted", cancellationToken: ct);
    }

    [Test]
    public async Task Http_FirstEventDifferent_FailsWithObservedFirstEvent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(CycleThenComplete, "text/event-stream");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasFirstSseEvent("cycleCompleted", cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"cycleStarted\"");
    }

    [Test]
    public async Task Http_NullContentWithStrictFalse_FailsWithNoEventsMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasFirstSseEvent("anything", strictContentType: false, cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("no events");
    }

    [Test]
    public async Task Http_NonSseContentTypeWithStrictDefault_FailsWithUnexpectedContentType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(CycleThenComplete, "application/json");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasFirstSseEvent("cycleStarted", cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"text/event-stream\"");
        await Assert.That(ex.Message).Contains("application/json");
    }

    [Test]
    public async Task Http_StrictContentTypeFalse_BypassesValidation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(CycleThenComplete, "application/json");
        await Assert.That(response).HasFirstSseEvent("cycleStarted", strictContentType: false, cancellationToken: ct);
    }

    [Test]
    public async Task Http_NullResponse_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HttpResponseMessage nullResponse = null!;
        var ex = await Assert.That(async () => await nullResponse.HasFirstSseEvent("x", cancellationToken: ct))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    private static MemoryStream ToStream(string body) => new(Encoding.UTF8.GetBytes(body));

    private static HttpResponseMessage BuildResponse(string body, string contentType)
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
