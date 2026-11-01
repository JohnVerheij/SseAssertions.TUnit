using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SseAssertions.TUnit.Tests;

/// <summary>
/// A read-only stream whose bytes arrive when the test writes them, so a test controls exactly when a reader
/// sees each chunk: a deterministic stand-in for a server that streams events. Reads wait for the next chunk;
/// <see cref="Complete"/> ends the stream.
/// </summary>
internal sealed class ScriptedStream : Stream
{
    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
    private byte[] _current = [];
    private int _offset;

    /// <summary>Gets a value indicating whether the stream was disposed.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Makes <paramref name="text"/>, UTF-8 encoded, available as one chunk.</summary>
    /// <param name="text">The text.</param>
    public void Send(string text) => Send(Encoding.UTF8.GetBytes(text));

    /// <summary>Makes <paramref name="bytes"/> available as one chunk.</summary>
    /// <param name="bytes">The bytes.</param>
    public void Send(byte[] bytes) => _chunks.Writer.TryWrite(bytes);

    /// <summary>Ends the stream after the chunks already sent.</summary>
    public void Complete() => _chunks.Writer.TryComplete();

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_offset >= _current.Length)
        {
            if (!await _chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            if (_chunks.Reader.TryRead(out var next))
            {
                _current = next;
                _offset = 0;
            }
        }

        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>Not supported: the scripted stream is read asynchronously.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Flush()
    {
        // Read-only.
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        Complete();
        base.Dispose(disposing);
    }
}
