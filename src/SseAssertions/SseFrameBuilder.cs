using System;
using System.Globalization;
using System.Text;

namespace SseAssertions;

/// <summary>
/// Accumulates the fields of one Server-Sent Events frame, line by line, per the WHATWG / W3C SSE
/// specification. Shared by <see cref="SseFrameParser"/> (a whole body) and the incremental stream parser behind
/// <see cref="SseEventReader"/> and <see cref="SseStream"/>, so both apply the same rules.
/// </summary>
internal sealed class SseFrameBuilder
{
    private readonly StringBuilder _eventType = new();
    private readonly StringBuilder _data = new();
    private string? _id;
    private int? _retry;
    private bool _sawData;

    /// <summary>Processes one line, without its terminator.</summary>
    /// <param name="line">The line. Empty for the blank line that ends a frame.</param>
    /// <returns><see langword="true"/> when the line ended a frame (a blank line); take it with
    /// <see cref="TakeFrame"/>.</returns>
    public bool ProcessLine(ReadOnlySpan<char> line)
    {
        if (line.Length is 0)
        {
            return true;
        }

        if (line[0] is not ':')
        {
            // A line starting with a colon is a comment, ignored per spec.
            InterpretField(line);
        }

        return false;
    }

    /// <summary>Takes the accumulated frame and resets the builder for the next one.</summary>
    /// <param name="hadData">Whether the frame carried a <c>data:</c> line. Per spec a frame without one is not
    /// dispatched as an event.</param>
    /// <returns>The frame; its <see cref="SseEvent.Data"/> is empty when it had no data.</returns>
    public SseEvent TakeFrame(out bool hadData)
    {
        hadData = _sawData;

        // Strip the trailing LF appended after the final data line.
        if (_data.Length > 0 && _data[^1] is '\n')
        {
            _data.Length--;
        }

        var frame = new SseEvent(
            _eventType.Length > 0 ? _eventType.ToString() : "message",
            _data.ToString(),
            _id,
            _retry);

        _eventType.Clear();
        _data.Clear();
        _id = null;
        _retry = null;
        _sawData = false;
        return frame;
    }

    private void InterpretField(ReadOnlySpan<char> line)
    {
        var colon = line.IndexOf(':');
        string name;
        ReadOnlySpan<char> value;
        if (colon < 0)
        {
            name = line.ToString();
            value = default;
        }
        else
        {
            name = line[..colon].ToString();
            value = line[(colon + 1)..];
            if (value.Length > 0 && value[0] is ' ')
            {
                value = value[1..];
            }
        }

        switch (name)
        {
            case "event":
                _eventType.Clear();
                _eventType.Append(value);
                break;

            case "data":
                _data.Append(value);
                _data.Append('\n');
                _sawData = true;
                break;

            case "id" when value.IndexOf('\0') < 0:
                _id = value.ToString();
                break;

            case "retry" when TryParseRetry(value, out var parsed):
                _retry = parsed;
                break;

            default:
                // Any other field name is ignored per spec.
                break;
        }
    }

    private static bool TryParseRetry(ReadOnlySpan<char> value, out int parsed)
    {
        parsed = 0;
        if (value.Length is 0)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
    }
}
