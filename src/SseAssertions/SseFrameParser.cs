using System;
using System.Collections.Generic;

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
/// <c>event:</c>, <c>data:</c>, <c>id:</c>, and <c>retry:</c> directives are recognized; on a
/// blank line the accumulated frame is dispatched as an <see cref="SseEvent"/> (with
/// <see cref="SseEvent.EventName"/> defaulting to <c>"message"</c> per the WHATWG default when
/// no <c>event:</c> directive was present), unless no <c>data:</c> line was seen, in which case
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
    /// instances. Eagerly materialized; the parser does not retain a reference to
    /// <paramref name="body"/>.</summary>
    /// <param name="body">The SSE wire-format text. May be empty (returns an empty list).</param>
    /// <returns>The parsed events in document order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<SseEvent> Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var startOffset = body.Length > 0 && body[0] is '\uFEFF' ? 1 : 0;
        if (startOffset == body.Length)
        {
            return Array.Empty<SseEvent>();
        }

        var events = new List<SseEvent>();
        var frame = new SseFrameBuilder();
        ParseLines(body.AsSpan(startOffset), frame, events);

        // Per spec, the browser dispatches only on a blank line. Some servers omit the final
        // blank line; for an assertion library, dropping the trailing frame is strictly worse
        // than dispatching it: consumers expect to see the last event. Dispatch it when it has data.
        var last = frame.TakeFrame(out var hadData);
        if (hadData)
        {
            events.Add(last);
        }

        return events;
    }

    /// <summary>Splits <paramref name="text"/> into lines on any of the three terminators and feeds them to
    /// <paramref name="frame"/>, adding each completed frame that carries data.</summary>
    /// <param name="text">The body, without a byte order mark.</param>
    /// <param name="frame">The frame being accumulated; holds an unterminated last frame afterwards.</param>
    /// <param name="events">The events parsed so far.</param>
    private static void ParseLines(ReadOnlySpan<char> text, SseFrameBuilder frame, List<SseEvent> events)
    {
        while (!text.IsEmpty)
        {
            var terminator = text.IndexOfAny('\r', '\n');
            if (terminator < 0)
            {
                AddIfFrameEnded(frame, text, events);
                return;
            }

            AddIfFrameEnded(frame, text[..terminator], events);

            // Advance past the terminator; CRLF advances two chars.
            var crlf = text[terminator] is '\r' && terminator + 1 < text.Length && text[terminator + 1] is '\n';
            text = text[(terminator + (crlf ? 2 : 1))..];
        }
    }

    /// <summary>Feeds one line to <paramref name="frame"/> and adds the frame it ends, when it carries data.</summary>
    /// <param name="frame">The frame being accumulated.</param>
    /// <param name="line">The line, without its terminator.</param>
    /// <param name="events">The events parsed so far.</param>
    private static void AddIfFrameEnded(SseFrameBuilder frame, ReadOnlySpan<char> line, List<SseEvent> events)
    {
        if (!frame.ProcessLine(line))
        {
            return;
        }

        var ended = frame.TakeFrame(out var hadData);
        if (hadData)
        {
            events.Add(ended);
        }
    }
}
