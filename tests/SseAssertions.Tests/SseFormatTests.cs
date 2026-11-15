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

    [Test]
    public async Task LooksLikeServerSentEvents_CrFrameSeparator_ReturnsTrue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // CR alone is a line ending in the specification, and the parser reads this stream as one event.
        const string body = "event: ping\rdata: 1\r\r";

        await Assert.That(SseFormat.LooksLikeServerSentEvents(body)).IsTrue();
    }

    [Test]
    public async Task LooksLikeServerSentEvents_CrStreamWithTheFieldOnALaterLine_ReturnsTrue(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string body = ": a comment line\rdata: 1\r\r";

        await Assert.That(SseFormat.LooksLikeServerSentEvents(body)).IsTrue();
    }

    /// <summary>A blank line is found whichever line endings the stream mixes: the line after <c>data: 1</c> is
    /// empty in each of these bodies.</summary>
    [Test]
    [Arguments("data: 1\n\r\n")]
    [Arguments("data: 1\r\n\r")]
    [Arguments("data: 1\n\r")]
    [Arguments("data: 1\r\r\n")]
    public async Task LooksLikeServerSentEvents_BlankLineAfterAnotherLineEnding_ReturnsTrue(string body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await Assert.That(SseFormat.LooksLikeServerSentEvents(body)).IsTrue();
    }

    /// <summary>CRLF is one line ending, not a line ending followed by a blank line, and the text after the last line
    /// ending is no complete line, so none of these bodies has a blank line.</summary>
    [Test]
    [Arguments("data: 1\r\n")]
    [Arguments("data: 1\r\nid: 2\r\n")]
    [Arguments("data: 1\n")]
    public async Task LooksLikeServerSentEvents_NoBlankLine_ReturnsFalse(string body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await Assert.That(SseFormat.LooksLikeServerSentEvents(body)).IsFalse();
    }

    /// <summary>A leading byte order mark is skipped, as the parser skips it, so it does not hide the field on the
    /// first line; only one is skipped.</summary>
    [Test]
    public async Task LooksLikeServerSentEvents_LeadingByteOrderMark_IsSkipped(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await Assert.That(SseFormat.LooksLikeServerSentEvents("\uFEFFdata: 1\n\n")).IsTrue();
        await Assert.That(SseFormat.LooksLikeServerSentEvents("\uFEFF\uFEFFdata: 1\n\n")).IsFalse();
    }
}
