using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SseAssertions;

/// <summary>
/// Reads Server-Sent Events from a <see cref="Stream"/> one at a time, as they arrive. Use it for a stream that
/// never ends: read until the events you need have arrived, then stop, instead of waiting for the end of the
/// stream or a timeout. The parsing rules are those of <see cref="SseFrameParser.Parse"/>.
/// </summary>
/// <remarks>
/// <para>
/// The body is decoded as UTF-8, as the SSE specification requires; a leading byte order mark is skipped.
/// </para>
/// <para>
/// On an <see cref="SseStream"/>, every reader shares the stream's position: a reader created after
/// another one stopped continues with the next event, and no bytes are lost between them. On another stream, a
/// reader buffers ahead; once you stop using it, read the rest of the stream through the same reader.
/// </para>
/// <para>Not thread-safe.</para>
/// </remarks>
public sealed class SseEventReader
{
    private readonly SseStreamParser _parser;
    private readonly SseReadProgress _progress = new();

    /// <summary>Initializes a reader over <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream to read. An <see cref="SseStream"/> is read from its current
    /// position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public SseEventReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _parser = stream is SseStream sseStream ? sseStream.Parser : new SseStreamParser(stream);
    }

    /// <summary>Gets the number of bytes this reader has read from the stream.</summary>
    public long BytesRead => _progress.BytesRead;

    /// <summary>Gets the start of the text this reader has decoded (about the first 256 characters, lines
    /// joined by <c>\n</c>), for failure messages.</summary>
    public string TextExcerpt => _progress.Excerpt;

    /// <summary>Reads the next event, waiting until a complete one has arrived. A frame without a <c>data:</c>
    /// line is skipped, as the SSE specification requires.</summary>
    /// <param name="cancellationToken">Cancels the wait for the next event.</param>
    /// <returns>The next event, or <see langword="null"/> at the end of the stream.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled
    /// first.</exception>
    public async ValueTask<SseEvent?> ReadAsync(CancellationToken cancellationToken)
    {
        while (await _parser.ReadFrameAsync(_progress, cancellationToken).ConfigureAwait(false) is { } frame)
        {
            if (frame.HadData)
            {
                return frame.Event;
            }
        }

        return null;
    }
}
