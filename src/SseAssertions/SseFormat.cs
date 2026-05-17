using System;

namespace SseAssertions;

/// <summary>
/// Framework-agnostic primitives over Server-Sent Events streams. v0.0.1 ships the
/// <see cref="LooksLikeServerSentEvents(string)"/> discriminator only; the full frame parser
/// and per-frame JSON-payload deserialization helpers ship in v0.1.0.
/// </summary>
public static class SseFormat
{
    /// <summary>
    /// Lightweight discriminator that returns <see langword="true"/> when <paramref name="body"/>
    /// has the textual shape of a Server-Sent Events stream: at least one SSE field line (a line
    /// beginning with <c>event:</c>, <c>data:</c>, <c>id:</c>, or <c>retry:</c>) followed by the
    /// double-newline frame separator. Returns <see langword="false"/> for arbitrary strings,
    /// empty input, or text that contains no SSE field markers.
    /// </summary>
    /// <remarks>
    /// The check is intentionally cheap and forgiving: it does not parse the stream, validate
    /// the encoding, or enforce field ordering. Its purpose is to give a test assertion a fast
    /// "is this the right ballpark" signal before a structured-parse assertion shipped in a
    /// later release walks the actual frames.
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

        // SSE frame separator: a blank line ('\n\n', or '\r\n\r\n' for CRLF streams). Reject text
        // that lacks the separator entirely; SSE producers always terminate frames this way.
        if (body.IndexOf("\n\n", StringComparison.Ordinal) < 0
            && body.IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        // At least one SSE field marker must appear at a line start. Scan line-by-line; field
        // names per the WHATWG specification are case-sensitive ASCII.
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("event:", StringComparison.Ordinal)
                || trimmed.StartsWith("data:", StringComparison.Ordinal)
                || trimmed.StartsWith("id:", StringComparison.Ordinal)
                || trimmed.StartsWith("retry:", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
