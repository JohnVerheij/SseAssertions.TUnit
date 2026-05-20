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
/// End-to-end tests for the <c>HasSseEventsInOrder(params string[])</c> assertion across all three
/// receivers (chain on <see cref="string"/>, flat on <see cref="Stream"/> and
/// <see cref="HttpResponseMessage"/>), covering both non-strict (gaps allowed) and strict
/// (contiguous) ordering modes.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class HasSseEventsInOrderTests
{
    private const string ABC = "event: a\ndata: 1\n\nevent: b\ndata: 2\n\nevent: c\ndata: 3\n\n";
    private const string ABCWithHeartbeat = "event: a\ndata: 1\n\nevent: heartbeat\ndata: -\n\nevent: b\ndata: 2\n\nevent: c\ndata: 3\n\n";

    private static readonly string[] AbcNames = ["a", "b", "c"];
    private static readonly string[] AbNames = ["a", "b"];
    private static readonly string[] ANames = ["a"];
    private static readonly string[] BNames = ["b"];
    private static readonly string[] CaNames = ["c", "a"];
    private static readonly string[] AzNames = ["a", "z"];
    private static readonly string[] EmptyNames = [];

    [Test]
    public async Task String_ContiguousMatch_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(ABC).HasSseEventsInOrder(AbcNames);
    }

    [Test]
    public async Task String_GapsBetweenMatches_NonStrictPasses(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(ABCWithHeartbeat).HasSseEventsInOrder(AbcNames);
    }

    [Test]
    public async Task String_ReorderedSequence_FailsWithOutOfOrder(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(ABC).HasSseEventsInOrder(CaNames);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"a\"");
        await Assert.That(ex.Message).Contains("appeared before");
    }

    [Test]
    public async Task String_MissingEvent_FailsWithNotInStream(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(ABC).HasSseEventsInOrder(AzNames);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"z\"");
        await Assert.That(ex.Message).Contains("not in the stream");
    }

    [Test]
    public async Task String_StrictContiguousMatch_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(ABC).HasSseEventsInOrder(AbcNames).WithStrictOrdering();
    }

    [Test]
    public async Task String_StrictWithInterveningEvent_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
        {
            await Assert.That(ABCWithHeartbeat).HasSseEventsInOrder(AbcNames).WithStrictOrdering();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("contiguously");
        await Assert.That(ex.Message).Contains("heartbeat");
    }

    [Test]
    public async Task String_EmptyEventNamesArray_TriviallyPasses(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(ABC).HasSseEventsInOrder(EmptyNames);
    }

    [Test]
    public async Task String_SingleEvent_PassesWhenPresentAnywhere(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(ABC).HasSseEventsInOrder(BNames);
    }

    [Test]
    public async Task Stream_GapsBetweenMatches_NonStrictPasses(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(ABCWithHeartbeat);
        await Assert.That(stream).HasSseEventsInOrder(AbcNames, cancellationToken: ct);
    }

    [Test]
    public async Task Stream_StrictWithInterveningEvent_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = ToStream(ABCWithHeartbeat);
        var ex = await Assert.That(async () =>
        {
            await Assert.That(stream).HasSseEventsInOrder(AbcNames, strictOrdering: true, cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("contiguously");
    }

    [Test]
    public async Task Http_GapsBetweenMatches_NonStrictPasses(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ABCWithHeartbeat, "text/event-stream");
        await Assert.That(response).HasSseEventsInOrder(AbcNames, cancellationToken: ct);
    }

    [Test]
    public async Task Http_StrictContiguousMatch_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ABC, "text/event-stream");
        await Assert.That(response).HasSseEventsInOrder(AbcNames, strictOrdering: true, cancellationToken: ct);
    }

    [Test]
    public async Task Http_NonSseContentTypeWithStrictDefault_FailsWithUnexpectedContentType(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ABC, "application/json");
        var ex = await Assert.That(async () =>
        {
            await Assert.That(response).HasSseEventsInOrder(AbNames, cancellationToken: ct);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("\"text/event-stream\"");
    }

    [Test]
    public async Task Http_StrictContentTypeFalse_BypassesValidation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ABC, "application/json");
        await Assert.That(response).HasSseEventsInOrder(AbNames, strictContentType: false, cancellationToken: ct);
    }

    [Test]
    public async Task String_NullBody_FailsWithReceiverNullMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string nullBody = null!;
        var ex = await Assert.That(async () =>
        {
            await Assert.That(nullBody).HasSseEventsInOrder(ANames);
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("null");
    }

    [Test]
    public async Task Stream_NullStream_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream nullStream = null!;
        var ex = await Assert.That(async () => await nullStream.HasSseEventsInOrder(ANames, cancellationToken: ct))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task Http_NullEventNamesArray_ThrowsArgumentNullException(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var response = BuildResponse(ABC, "text/event-stream");
        string[] nullNames = null!;
        var ex = await Assert.That(async () => await response.HasSseEventsInOrder(nullNames, cancellationToken: ct))
            .Throws<System.ArgumentNullException>();
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task String_StrictWithFewerEventsThanExpected_FailsWithCountMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string twoEvents = "event: a\ndata: 1\n\nevent: b\ndata: 2\n\n";
        var ex = await Assert.That(async () =>
        {
            await Assert.That(twoEvents).HasSseEventsInOrder(AbcNames).WithStrictOrdering();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("contiguously");
        await Assert.That(ex.Message).Contains("only 2 event(s)");
    }

    [Test]
    public async Task String_StrictWithFirstNameNotInStream_FailsWithNotInStreamMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string bcd = "event: b\ndata: 1\n\nevent: c\ndata: 2\n\nevent: d\ndata: 3\n\n";
        var ex = await Assert.That(async () =>
        {
            await Assert.That(bcd).HasSseEventsInOrder(AbcNames).WithStrictOrdering();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("contiguously");
        await Assert.That(ex.Message).Contains("\"a\" was not in the stream");
    }

    [Test]
    public async Task String_StrictPartialMatchThenStreamExhausted_FailsWithPartialPrefixDiagnostic(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Stream: [x, a, b]. Strict sequence [a, b, c] matches as far as [a, b] starting at
        // index 1, then the stream ends before 'c' can appear. The diagnostic should name the
        // matched prefix and its starting index; it must NOT report "a was not in the stream"
        // because 'a' is present at index 1.
        const string xab = "event: x\ndata: 0\n\nevent: a\ndata: 1\n\nevent: b\ndata: 2\n\n";
        var ex = await Assert.That(async () =>
        {
            await Assert.That(xab).HasSseEventsInOrder(AbcNames).WithStrictOrdering();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("contiguously");
        await Assert.That(ex.Message).Contains("ended after matching prefix [a, b]");
        await Assert.That(ex.Message).Contains("starting at index 1");
        // Sanity: must not regress to the misleading "was not in the stream" fallback.
        await Assert.That(ex.Message).DoesNotContain("was not in the stream");
    }

    private static MemoryStream ToStream(string body) => new(Encoding.UTF8.GetBytes(body));

    private static HttpResponseMessage BuildResponse(string body, string contentType)
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
