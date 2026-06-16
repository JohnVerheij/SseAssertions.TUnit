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
/// End-to-end tests for the flat <c>HasSseEvent(eventName, minCount, strictContentType,
/// cancellationToken)</c> entry point on the <see cref="HttpResponseMessage"/> receiver. Covers
/// the happy-path body read, the Content-Type validation contract, the
/// <c>strictContentType: false</c> opt-out, and argument validation.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseHasEventHttpTests
{
    private const string ThreeTicks = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\nevent: tick\ndata: 3\n\n";

    [Test]
    public async Task HasSseEvent_HttpResponseWithSseContentType_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ThreeTicks, "text/event-stream");

        await Assert.That(response).HasSseEvent("tick", cancellationToken: ct).AtLeast(2);
    }

    [Test]
    public async Task HasSseEvent_HttpResponseWithJsonContentType_FailsWithUnexpectedContentType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ThreeTicks, "application/json");

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEvent("tick", cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"text/event-stream\"");
        await Assert.That(ex.Message).Contains("application/json");
    }

    [Test]
    public async Task HasSseEvent_HttpResponseWithoutContentType_FailsWithAbsentToken(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ThreeTicks, contentType: null);

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEvent("tick", cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("<absent>");
    }

    [Test]
    public async Task HasSseEvent_StrictContentTypeFalse_BypassesValidation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ThreeTicks, "application/json");

        await Assert.That(response).HasSseEvent("tick", strictContentType: false, cancellationToken: ct);
    }

    [Test]
    public async Task HasSseEvent_ContentTypeCaseInsensitive(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ThreeTicks, "Text/Event-Stream");

        await Assert.That(response).HasSseEvent("tick", cancellationToken: ct);
    }

    [Test]
    public async Task HasSseEvent_DeclaredCharset_DecodesBodyUsingThatCharset(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: tick\ndata: café\n\n";
        var bytes = Encoding.Latin1.GetBytes(body);

        var content = new StreamContent(new MemoryStream(bytes));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/event-stream; charset=iso-8859-1");
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        await Assert.That(response).HasSseEvent("tick", cancellationToken: ct);
    }

    [Test]
    public async Task HasSseEvent_InvalidCharset_FallsBackToUtf8(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = Encoding.UTF8.GetBytes(ThreeTicks);

        var content = new StreamContent(new MemoryStream(bytes));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/event-stream; charset=not-a-real-charset");
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        await Assert.That(response).HasSseEvent("tick", cancellationToken: ct);
    }

    [Test]
    public async Task HasSseEvent_NullContentWithStrictFalse_FailsWithCountMismatchNotNre(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEvent("tick", strictContentType: false, cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("but observed: 0");
    }

    [Test]
    public async Task HasSseEvent_BelowAtLeast_FailsWithCountMismatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ThreeTicks, "text/event-stream");

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEvent("tick", cancellationToken: ct).AtLeast(5);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("at least 5");
    }

    [Test]
    public async Task HasSseEvent_NullResponse_FailsWithReceiverWasNull(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HttpResponseMessage nullResponse = null!;
        var ex = await Assert.That(async () => await Assert.That(nullResponse).HasSseEvent("tick"))
            .Throws<AssertionException>();
        await Assert.That(ex!.Message).Contains("receiver was null");
    }

    [Test]
    public async Task HasSseEvent_NullEventName_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ThreeTicks, "text/event-stream");
        string nullName = null!;
        var ex = await Assert.That(async () => await Assert.That(response).HasSseEvent(nullName))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task HasSseEvent_NegativeAtLeast_ThrowsArgumentOutOfRange(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ThreeTicks, "text/event-stream");
        var ex = await Assert.That(async () => await Assert.That(response).HasSseEvent("tick").AtLeast(-1))
            .Throws<System.ArgumentOutOfRangeException>();
        await Assert.That(ex).IsNotNull();
    }

    private static HttpResponseMessage BuildResponse(string body, string? contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new StreamContent(new MemoryStream(bytes));
        if (contentType is not null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
