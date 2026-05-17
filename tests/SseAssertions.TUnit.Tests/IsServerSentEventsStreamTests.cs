using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions.Exceptions;

namespace SseAssertions.TUnit.Tests;

/// <summary>
/// End-to-end tests for the <c>IsServerSentEventsStream()</c> fluent entry point: TUnit's
/// <c>[GenerateAssertion]</c> source generator emits the chain method on <see cref="string"/>,
/// and the adapter delegates to <see cref="SseAssertions.SseFormat.LooksLikeServerSentEvents(string)"/>.
/// v0.0.1 covers the success path and a representative failure path.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class IsServerSentEventsStreamTests
{
    [Test]
    public async Task IsServerSentEventsStream_ValidStream_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\n";

        await Assert.That(body).IsServerSentEventsStream();
    }

    [Test]
    public async Task IsServerSentEventsStream_PlainTextWithoutFieldMarker_FailsWithShapeMessage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "hello world\n\nthis is not an SSE stream\n\n";

        var ex = await Assert.That(async () =>
        {
            await Assert.That(body).IsServerSentEventsStream();
        }).Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("Server-Sent Events");
        await Assert.That(ex.Message).Contains("SSE field marker");
    }
}
