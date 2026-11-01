using System.Text;

namespace SseAssertions;

/// <summary>Counts the bytes a reader pulled from the stream and keeps the start of the text it decoded, for
/// failure messages.</summary>
internal sealed class SseReadProgress
{
    private const int ExcerptLimit = 256;
    private readonly StringBuilder _excerpt = new();

    /// <summary>Gets the number of bytes read from the stream.</summary>
    public long BytesRead { get; private set; }

    /// <summary>Gets the start of the decoded text, at most a little over 256 characters, lines joined by
    /// <c>\n</c>.</summary>
    public string Excerpt => _excerpt.ToString();

    /// <summary>Records bytes read from the stream.</summary>
    /// <param name="count">The number of bytes.</param>
    public void OnBytes(int count) => BytesRead += count;

    /// <summary>Records one decoded line.</summary>
    /// <param name="line">The line, without its terminator.</param>
    public void OnText(string line)
    {
        if (_excerpt.Length < ExcerptLimit)
        {
            _excerpt.Append(line).Append('\n');
        }
    }
}
