using System;
using System.Linq;

namespace SseAssertions;

/// <summary>
/// Framework-agnostic format check over Server-Sent Events text: the
/// <see cref="LooksLikeServerSentEvents(string)"/> discriminator. Parsing is in <see cref="SseFrameParser"/>
/// (a whole body) and <see cref="SseEventReader"/> (a stream, event by event).
/// </summary>
public static class SseFormat
{
    /// <summary>The SSE field markers, per the WHATWG specification (case-sensitive ASCII).</summary>
    private static readonly string[] FieldMarkers = ["event:", "data:", "id:", "retry:"];

    /// <summary>The line endings of the WHATWG specification, CRLF first so it counts as one.</summary>
    private static readonly string[] LineEndings = ["\r\n", "\r", "\n"];

    /// <summary>
    /// Lightweight discriminator that returns <see langword="true"/> when <paramref name="body"/>
    /// has the textual shape of a Server-Sent Events stream: at least one SSE field line (a line
    /// beginning with <c>event:</c>, <c>data:</c>, <c>id:</c>, or <c>retry:</c>) and a blank line, the frame
    /// separator. Lines may end in LF, CRLF or CR, as the WHATWG specification allows and the parser accepts.
    /// Returns <see langword="false"/> for arbitrary strings, empty input, or text that contains no SSE field
    /// markers.
    /// </summary>
    /// <remarks>
    /// The check is intentionally cheap and forgiving: it does not parse the stream, validate
    /// the encoding, or enforce field ordering. Its purpose is to give a test assertion a fast
    /// "is this the right ballpark" signal before an assertion that parses the frames runs.
    /// </remarks>
    /// <param name="body">The text to inspect.</param>
    /// <returns><see langword="true"/> if the input has the shape of an SSE stream; otherwise
    /// <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    public static bool LooksLikeServerSentEvents(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.Length is 0)
        {
            return false;
        }

        // SSE frame separator: a blank line ('\n\n', '\r\n\r\n' for CRLF streams, '\r\r' for CR streams). Reject
        // text that lacks the separator entirely; SSE producers always terminate frames this way.
        if (body.IndexOf("\n\n", StringComparison.Ordinal) < 0
            && body.IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0
            && body.IndexOf("\r\r", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        // At least one SSE field marker must appear at a line start. Scan line-by-line, splitting at every
        // line ending the specification allows; field names are case-sensitive ASCII.
        return body.Split(LineEndings, StringSplitOptions.None).Any(static line => StartsWithField(line));
    }

    /// <summary>Gets whether <paramref name="line"/> starts with one of the SSE field markers.</summary>
    private static bool StartsWithField(string line)
        => FieldMarkers.Any(marker => line.StartsWith(marker, StringComparison.Ordinal));
}
