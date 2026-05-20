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
/// Tests for the synchronous <c>HasSseContentType(bool strict)</c> header-only discriminator on
/// the <see cref="HttpResponseMessage"/> receiver. The assertion does not read the body; it only
/// inspects <c>HttpContent.Headers.ContentType</c>. Distinct from
/// <c>HasSseEvent(strictContentType)</c>, which validates the same header alongside a body read.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class HasSseContentTypeTests
{
    [Test]
    public async Task NonStrict_ExactSseMediaType_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse("text/event-stream");

        await Assert.That(response).HasSseContentType();
    }

    [Test]
    public async Task NonStrict_SseMediaTypeWithCharsetParameter_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse("text/event-stream; charset=utf-8");

        await Assert.That(response).HasSseContentType();
    }

    [Test]
    public async Task NonStrict_CaseInsensitiveMediaType_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse("Text/Event-Stream");

        await Assert.That(response).HasSseContentType();
    }

    [Test]
    public async Task NonStrict_JsonMediaType_FailsWithStartingWithMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse("application/json");

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseContentType();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("starting with \"text/event-stream\"");
        await Assert.That(ex.Message).Contains("application/json");
    }

    [Test]
    public async Task NonStrict_NoContentTypeHeader_FailsWithAbsentToken(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(contentType: null);

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseContentType();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("<absent>");
        await Assert.That(ex.Message).Contains("starting with \"text/event-stream\"");
    }

    [Test]
    public async Task NonStrict_NullContent_FailsWithAbsentToken(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseContentType();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("<absent>");
    }

    [Test]
    public async Task Strict_ExactSseMediaTypeNoParameters_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse("text/event-stream");

        await Assert.That(response).HasSseContentType(strict: true);
    }

    [Test]
    public async Task Strict_SseMediaTypeWithCharsetParameter_FailsWithExactlyMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse("text/event-stream; charset=utf-8");

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseContentType(strict: true);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("exactly \"text/event-stream\"");
        await Assert.That(ex.Message).Contains("charset=utf-8");
    }

    [Test]
    public async Task Strict_CaseInsensitiveMediaTypeNoParameters_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse("Text/Event-Stream");

        await Assert.That(response).HasSseContentType(strict: true);
    }

    [Test]
    public async Task Strict_NonSseMediaType_FailsWithExactlyMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse("application/json");

        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseContentType(strict: true);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("exactly \"text/event-stream\"");
        await Assert.That(ex.Message).Contains("application/json");
    }

    [Test]
    public async Task NullResponse_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HttpResponseMessage nullResponse = null!;

        var ex = await Assert.That(async () => await Task.Run(() => nullResponse.HasSseContentType()))
            .Throws<System.ArgumentNullException>();

        await Assert.That(ex).IsNotNull();
    }

    private static HttpResponseMessage BuildResponse(string? contentType)
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("dummy")));
        if (contentType is not null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
