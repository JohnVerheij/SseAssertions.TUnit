using System.Threading;
using System.Threading.Tasks;
using SseAssertions;

namespace SseAssertions.Tests;

/// <summary>
/// Framework-agnostic tests for <see cref="SseFormat.LooksLikeServerSentEvents(string)"/>: the
/// v0.0.1 discriminator that signals whether a string has the shape of a Server-Sent Events
/// stream. Structured per-frame parsing arrives in a later release.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseFormatTests
{
    [Test]
    public async Task LooksLikeServerSentEvents_StreamWithDataFrame_ReturnsTrue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: message\ndata: hello\n\n";

        await Assert.That(SseFormat.LooksLikeServerSentEvents(body)).IsTrue();
    }

    [Test]
    public async Task LooksLikeServerSentEvents_PlainTextWithoutFieldMarker_ReturnsFalse(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "hello\n\nworld\n\n";

        await Assert.That(SseFormat.LooksLikeServerSentEvents(body)).IsFalse();
    }

    [Test]
    public async Task LooksLikeServerSentEvents_FieldMarkerWithoutFrameSeparator_ReturnsFalse(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "data: hello";

        await Assert.That(SseFormat.LooksLikeServerSentEvents(body)).IsFalse();
    }

    [Test]
    public async Task LooksLikeServerSentEvents_EmptyString_ReturnsFalse(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(SseFormat.LooksLikeServerSentEvents(string.Empty)).IsFalse();
    }

    [Test]
    public async Task LooksLikeServerSentEvents_CrLfFrameSeparator_ReturnsTrue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = "event: ping\r\ndata: 1\r\n\r\n";

        await Assert.That(SseFormat.LooksLikeServerSentEvents(body)).IsTrue();
    }
}
