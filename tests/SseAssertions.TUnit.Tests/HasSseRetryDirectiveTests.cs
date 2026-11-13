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
/// End-to-end tests for the <c>HasSseRetryDirective(int? millis)</c> assertion across all three
/// receivers (<see cref="string"/>, <see cref="Stream"/>, <see cref="HttpResponseMessage"/>).
/// Covers any-value (null) and exact-match modes, multiple-directive any-match semantics, and
/// argument validation.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class HasSseRetryDirectiveTests
{
    private const string WithRetry5000 = "retry: 5000\nevent: tick\ndata: 1\n\n";
    private const string WithRetry3000 = "retry: 3000\nevent: tick\ndata: 1\n\n";
    private const string NoRetry = "event: tick\ndata: 1\n\n";
    private const string MultipleRetries = "retry: 3000\nevent: tick\ndata: 1\n\nretry: 5000\nevent: tick\ndata: 2\n\n";

    [Test]
    public async Task String_AnyValueMatch_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(WithRetry5000).HasSseRetryDirective();
    }

    [Test]
    public async Task String_ExactValueMatch_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(WithRetry5000).HasSseRetryDirective(5000);
    }

    [Test]
    public async Task String_ExactValueMismatch_FailsWithObservedValues(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(WithRetry3000).HasSseRetryDirective(5000);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("none equal to 5000");
        await Assert.That(ex.Message).Contains("3000");
    }

    [Test]
    public async Task String_NoRetryDirective_AnyMode_FailsWithNoRetryMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(NoRetry).HasSseRetryDirective();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("no retry directive was found");
    }

    [Test]
    public async Task String_NoRetryDirective_ExactMode_FailsWithNoRetryMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(NoRetry).HasSseRetryDirective(5000);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("but no retry directive was found");
    }

    [Test]
    public async Task String_MultipleRetryDirectives_OneMatches_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(MultipleRetries).HasSseRetryDirective(5000);
    }

    [Test]
    public async Task String_MultipleRetryDirectives_NoneMatch_FailsListingAllObserved(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(MultipleRetries).HasSseRetryDirective(9999);
        }).Throws<AssertionException>();

        // The failure message should list both observed values, comma-separated.
        await Assert.That(ex!.Message).Contains("none equal to 9999");
        await Assert.That(ex.Message).Contains("3000");
        await Assert.That(ex.Message).Contains("5000");
        await Assert.That(ex.Message).Contains(", ");
    }

    [Test]
    public async Task String_NullBody_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string nullBody = null!;
        var ex = await Assert.That(async () => await Task.Run(() => nullBody.HasSseRetryDirective(), ct))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task Stream_ExactValueMatch_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(WithRetry5000);
        await Assert.That(stream).HasSseRetryDirective(5000, cancellationToken: ct);
    }

    [Test]
    public async Task Stream_NoRetryDirective_AnyMode_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(NoRetry);
        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseRetryDirective(cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("no retry directive was found");
    }

    [Test]
    public async Task Stream_NullStream_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream nullStream = null!;
        var ex = await Assert.That(async () => await nullStream.HasSseRetryDirective(cancellationToken: ct))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task Http_ExactValueMatch_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(WithRetry5000, "text/event-stream");
        await Assert.That(response).HasSseRetryDirective(5000, cancellationToken: ct);
    }

    [Test]
    public async Task Http_NonSseContentTypeWithStrictDefault_FailsWithUnexpectedContentType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(WithRetry5000, "application/json");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseRetryDirective(5000, cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"text/event-stream\"");
    }

    [Test]
    public async Task Http_StrictContentTypeFalse_BypassesValidation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(WithRetry5000, "application/json");
        await Assert.That(response).HasSseRetryDirective(5000, strictContentType: false, cancellationToken: ct);
    }

    [Test]
    public async Task Http_NullResponse_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HttpResponseMessage nullResponse = null!;
        var ex = await Assert.That(async () => await nullResponse.HasSseRetryDirective(cancellationToken: ct))
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

    /// <summary>A <c>retry:</c> field in a frame without <c>data:</c> counts: a server commonly opens the stream with
    /// a bare directive, and a browser applies it. The dispatched events do not carry it, so the assertion reads the
    /// wire-level fields.</summary>
    [Test]
    public async Task BareRetryFrame_CountsOnEveryReceiver(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "retry: 3000\n\nevent: tick\ndata: 1\n\n";
        using var stream = ToStream(body);
        using var response = BuildResponse(body, "text/event-stream");

        await Assert.That(body).HasSseRetryDirective(3000);
        await Assert.That(stream).HasSseRetryDirective(3000, ct);
        await Assert.That(response).HasSseRetryDirective(3000, cancellationToken: ct);
    }

    /// <summary>A directive after the data, an invalid value and a field that only starts with "retry" are told apart.</summary>
    [Test]
    public async Task RetryFields_ValueRules(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await Assert.That("data: x\nretry:42\n\n").HasSseRetryDirective(42);
        await Assert.That("data: x\r\nretry: 7\r\n\r\n").HasSseRetryDirective(7);
        await Assert.That(async () => await Assert.That("data: x\nretry: soon\nretryx: 5\nretry\n\n").HasSseRetryDirective())
            .Throws<AssertionException>();
        await Assert.That(async () => await Assert.That(": retry: 5\ndata: x\n\n").HasSseRetryDirective())
            .Throws<AssertionException>();
    }
}
