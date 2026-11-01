using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SseAssertions;

/// <summary>
/// Incremental Server-Sent Events parser over a <see cref="Stream"/>. Splits lines at the byte level (in UTF-8,
/// <c>\r</c> and <c>\n</c> are single bytes that never occur inside another character), so the bytes after the
/// last line it consumed stay raw in its buffer: a frame-level reader can stop at a frame boundary and a later
/// reader, or a raw read through <see cref="SseStream"/>, continues exactly there.
/// </summary>
/// <remarks>Not thread-safe: one reader at a time.</remarks>
internal sealed class SseStreamParser
{
    private const int InitialBufferSize = 4096;
    private const byte CarriageReturn = 0x0D;
    private const byte LineFeed = 0x0A;

    private readonly Stream _stream;
    private readonly SseFrameBuilder _frame = new();
    private byte[] _buffer = new byte[InitialBufferSize];
    private int _start;
    private int _end;
    private bool _atStart = true;
    private bool _skipLineFeed;
    private bool _streamEnded;
    private bool _finalFrameTaken;

    /// <summary>Initializes a parser over <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream to parse.</param>
    public SseStreamParser(Stream stream) => _stream = stream;

    /// <summary>
    /// Reads the next frame: every block of lines ended by a blank line, including one without data, and at the
    /// end of the stream an unterminated final frame when it carries data (as <see cref="SseFrameParser.Parse"/>
    /// does).
    /// </summary>
    /// <param name="progress">Receives the bytes read and the text decoded, for diagnostics.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The frame, or <see langword="null"/> at the end of the stream.</returns>
    public async ValueTask<SseFrame?> ReadFrameAsync(SseReadProgress progress, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryTakeLine(progress, out var line))
            {
                if (_frame.ProcessLine(line))
                {
                    var frame = _frame.TakeFrame(out var hadData);
                    return new SseFrame(frame, hadData);
                }

                continue;
            }

            if (_streamEnded)
            {
                return TakeFinalFrame(progress);
            }

            await FillAsync(progress, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads raw bytes: first those buffered past the last consumed line, then from the stream.</summary>
    /// <param name="destination">The buffer to fill.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of bytes read; 0 at the end of the stream.</returns>
    public async ValueTask<int> ReadRawAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        while (true)
        {
            var buffered = TakeBuffered(destination.Span);
            if (buffered > 0 || destination.IsEmpty || _streamEnded)
            {
                return buffered;
            }

            var read = await _stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
            if (read > 0 && DropPendingLineFeed(destination.Span[..read], ref read))
            {
                continue;
            }

            _atStart = false;
            return read;
        }
    }

    /// <summary>Reads raw bytes synchronously: first those buffered past the last consumed line, then from the
    /// stream.</summary>
    /// <param name="destination">The buffer to fill.</param>
    /// <returns>The number of bytes read; 0 at the end of the stream.</returns>
    public int ReadRaw(Span<byte> destination)
    {
        while (true)
        {
            var buffered = TakeBuffered(destination);
            if (buffered > 0 || destination.IsEmpty || _streamEnded)
            {
                return buffered;
            }

            var read = _stream.Read(destination);
            if (read > 0 && DropPendingLineFeed(destination[..read], ref read))
            {
                continue;
            }

            _atStart = false;
            return read;
        }
    }

    /// <summary>Copies buffered bytes into <paramref name="destination"/>, dropping the line feed of a CRLF whose
    /// carriage return ended the last consumed line.</summary>
    private int TakeBuffered(Span<byte> destination)
    {
        if (_skipLineFeed && _start < _end)
        {
            _skipLineFeed = false;
            if (_buffer[_start] is LineFeed)
            {
                _start++;
            }
        }

        var count = Math.Min(destination.Length, _end - _start);
        _buffer.AsSpan(_start, count).CopyTo(destination);
        _start += count;
        if (count > 0)
        {
            _atStart = false;
        }

        return count;
    }

    /// <summary>Drops a leading line feed from freshly read bytes when the last consumed line ended with a carriage
    /// return whose line feed had not arrived yet.</summary>
    /// <returns><see langword="true"/> when nothing is left after dropping it, so the caller reads again.</returns>
    private bool DropPendingLineFeed(Span<byte> read, ref int count)
    {
        if (!_skipLineFeed)
        {
            return false;
        }

        _skipLineFeed = false;
        if (read[0] is not LineFeed)
        {
            return false;
        }

        read[1..].CopyTo(read);
        count--;
        return count is 0;
    }

    /// <summary>Takes the next complete line from the buffer, if one is there.</summary>
    private bool TryTakeLine(SseReadProgress progress, out string line)
    {
        line = string.Empty;
        if (_skipLineFeed && _start < _end)
        {
            _skipLineFeed = false;
            if (_buffer[_start] is LineFeed)
            {
                _start++;
            }
        }

        if (_atStart && !TrySkipByteOrderMark())
        {
            return false;
        }

        var pending = _buffer.AsSpan(_start, _end - _start);
        var terminator = pending.IndexOfAny(CarriageReturn, LineFeed);
        if (terminator < 0)
        {
            return false;
        }

        var consumed = terminator + 1;
        if (pending[terminator] is CarriageReturn)
        {
            if (terminator + 1 < pending.Length)
            {
                consumed += pending[terminator + 1] is LineFeed ? 1 : 0;
            }
            else
            {
                // The line feed of a CRLF may arrive with the next read.
                _skipLineFeed = true;
            }
        }

        line = Encoding.UTF8.GetString(pending[..terminator]);
        progress.OnText(line);
        _start += consumed;
        return true;
    }

    /// <summary>Skips a UTF-8 byte order mark at the start of the stream. Waits for more bytes only while the
    /// buffered ones could still be the start of one.</summary>
    /// <returns><see langword="true"/> when the start of the stream is settled.</returns>
    private bool TrySkipByteOrderMark()
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        var pending = _buffer.AsSpan(_start, _end - _start);
        if (pending.Length < bom.Length && !_streamEnded && bom.StartsWith(pending))
        {
            return false;
        }

        if (pending.StartsWith(bom))
        {
            _start += bom.Length;
        }

        _atStart = false;
        return true;
    }

    /// <summary>At the end of the stream, processes an unterminated last line and returns the final frame once,
    /// when it carries data.</summary>
    private SseFrame? TakeFinalFrame(SseReadProgress progress)
    {
        if (_finalFrameTaken)
        {
            return null;
        }

        _finalFrameTaken = true;
        if (_start < _end)
        {
            var line = Encoding.UTF8.GetString(_buffer, _start, _end - _start);
            progress.OnText(line);
            _start = _end;
            _frame.ProcessLine(line);
        }

        var frame = _frame.TakeFrame(out var hadData);
        return hadData ? new SseFrame(frame, HadData: true) : null;
    }

    /// <summary>Reads more bytes into the buffer, making room first.</summary>
    private async ValueTask FillAsync(SseReadProgress progress, CancellationToken cancellationToken)
    {
        if (_start > 0)
        {
            _buffer.AsSpan(_start, _end - _start).CopyTo(_buffer);
            _end -= _start;
            _start = 0;
        }

        if (_end == _buffer.Length)
        {
            Array.Resize(ref _buffer, _buffer.Length * 2);
        }

        var read = await _stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken).ConfigureAwait(false);
        if (read is 0)
        {
            _streamEnded = true;
            return;
        }

        _end += read;
        progress.OnBytes(read);
    }
}
