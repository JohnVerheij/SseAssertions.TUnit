using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SseAssertions;

/// <summary>
/// Parses a Server-Sent Events wire-format string into a sequence of <see cref="SseEvent"/>
/// instances per the WHATWG / W3C SSE specification.
/// </summary>
/// <remarks>
/// <para>
/// The parser handles all three line terminators (<c>\r\n</c>, <c>\r</c>, <c>\n</c>), strips a
/// single leading UTF-8 BOM (<c>U+FEFF</c>) when present at offset 0, ignores comment lines
/// (lines starting with <c>:</c>), and treats unknown field names as no-ops per spec. The
/// <c>event:</c>, <c>data:</c>, <c>id:</c>, and <c>retry:</c> directives are recognised; on a
/// blank line the accumulated frame is dispatched as an <see cref="SseEvent"/> (with
/// <see cref="SseEvent.EventName"/> defaulting to <c>"message"</c> per the WHATWG default when
/// no <c>event:</c> directive was present), unless no <c>data:</c> line was seen — in which case
/// the frame is dropped per spec.
/// </para>
/// <para>
/// Per-event semantics for <c>id:</c> and <c>retry:</c>: an <see cref="SseEvent"/> carries an
/// <see cref="SseEvent.Id"/> or <see cref="SseEvent.RetryMillis"/> value only when the
/// corresponding directive appeared in that frame. This is a small but deliberate deviation from
/// the browser model (where last-event-id and reconnection-time are stream-wide state): the
/// assertion library exposes the wire-format directives as observed, so consumers can write
/// <c>HasSseEvent("retry").WithRetryMillis(r => r == 5000)</c>-style assertions.
/// </para>
/// </remarks>
public static class SseFrameParser
{
    /// <summary>Parses an SSE wire-format string into a sequence of <see cref="SseEvent"/>
    /// instances. Eagerly materialised; the parser does not retain a reference to
    /// <paramref name="body"/>.</summary>
    /// <param name="body">The SSE wire-format text. May be empty (returns an empty list).</param>
    /// <returns>The parsed events in document order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<SseEvent> Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.Length is 0)
        {
            return Array.Empty<SseEvent>();
        }

        var startOffset = body[0] is '\uFEFF' ? 1 : 0;
        if (startOffset == body.Length)
        {
            return Array.Empty<SseEvent>();
        }

        var state = new ParserState();
        ParseFrames(body.AsSpan(startOffset), ref state);

        // Per spec, the browser dispatches only on a blank line. Some servers omit the final
        // blank line; for an assertion library, dropping the trailing frame is strictly worse
        // than dispatching it — consumers expect to see the last event. Dispatch unconditionally
        // when data was seen.
        DispatchEvent(ref state);
        return state.Events;
    }

    private static void ParseFrames(ReadOnlySpan<char> body, ref ParserState state)
    {
        var lineStart = 0;
        var i = 0;
        while (i < body.Length)
        {
            var c = body[i];
            if (c is not '\r' and not '\n')
            {
                i++;
                continue;
            }

            ProcessLine(body[lineStart..i], ref state);

            // Advance past the terminator; CRLF advances two chars.
            var step = c is '\r' && i + 1 < body.Length && body[i + 1] is '\n' ? 2 : 1;
            i += step;
            lineStart = i;
        }

        // Trailing line (no terminator): hand it to the dispatcher; it remains for the caller
        // to flush the final event via DispatchEvent after ParseFrames returns.
        if (lineStart < body.Length)
        {
            ProcessLine(body[lineStart..], ref state);
        }
    }

    private static void ProcessLine(ReadOnlySpan<char> line, ref ParserState state)
    {
        if (line.Length is 0)
        {
            DispatchEvent(ref state);
            return;
        }

        if (line[0] is ':')
        {
            // Comment line — ignored per spec.
            return;
        }

        InterpretField(line, ref state);
    }

    private static void InterpretField(ReadOnlySpan<char> line, ref ParserState state)
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
                state.EventTypeBuffer.Clear();
                state.EventTypeBuffer.Append(value);
                break;

            case "data":
                state.DataBuffer.Append(value);
                state.DataBuffer.Append('\n');
                state.SawData = true;
                break;

            case "id" when value.IndexOf('\0') < 0:
                state.IdBuffer = value.ToString();
                break;

            case "retry" when TryParseRetry(value, out var parsed):
                state.RetryBuffer = parsed;
                break;

            default:
                // Unknown field — ignored per WHATWG spec.
                break;
        }
    }

    private static bool TryParseRetry(ReadOnlySpan<char> value, out int parsed)
    {
        if (value.Length is 0)
        {
            parsed = 0;
            return false;
        }

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
            {
                parsed = 0;
                return false;
            }
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
    }

    private static void DispatchEvent(ref ParserState state)
    {
        if (!state.SawData)
        {
            state.EventTypeBuffer.Clear();
            state.DataBuffer.Clear();
            state.IdBuffer = null;
            state.RetryBuffer = null;
            return;
        }

        // Strip the trailing LF the parser appended after the final data line.
        if (state.DataBuffer.Length > 0 && state.DataBuffer[^1] is '\n')
        {
            state.DataBuffer.Length--;
        }

        var eventName = state.EventTypeBuffer.Length > 0 ? state.EventTypeBuffer.ToString() : "message";
        state.Events.Add(new SseEvent(eventName, state.DataBuffer.ToString(), state.IdBuffer, state.RetryBuffer));

        state.EventTypeBuffer.Clear();
        state.DataBuffer.Clear();
        state.SawData = false;
        state.IdBuffer = null;
        state.RetryBuffer = null;
    }

    private struct ParserState
    {
        public ParserState()
        {
            Events = [];
            EventTypeBuffer = new StringBuilder();
            DataBuffer = new StringBuilder();
            SawData = false;
            IdBuffer = null;
            RetryBuffer = null;
        }

        public List<SseEvent> Events { get; }

        public StringBuilder EventTypeBuffer { get; }

        public StringBuilder DataBuffer { get; }

        public bool SawData { get; set; }

        public string? IdBuffer { get; set; }

        public int? RetryBuffer { get; set; }
    }
}
