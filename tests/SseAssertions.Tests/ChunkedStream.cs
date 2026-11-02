using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SseAssertions.Tests;

/// <summary>A stream over fixed bytes that returns at most <c>chunkSize</c> bytes per read, so a reader sees the
/// body split at every chunk boundary.</summary>
internal sealed class ChunkedStream(byte[] data, int chunkSize) : MemoryStream(data)
{
    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, chunkSize));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, chunkSize)]);

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
}
