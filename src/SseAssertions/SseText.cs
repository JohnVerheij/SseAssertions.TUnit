using System;

namespace SseAssertions;

/// <summary>Text helpers shared by the failure messages of both packages (this file is also compiled into
/// <c>SseAssertions.TUnit</c>).</summary>
internal static class SseText
{
    /// <summary>Replaces each carriage return and line feed in <paramref name="value"/> with the two-character
    /// escapes <c>\r</c> and <c>\n</c>, so a value rendered inside a one-line clause or a list row of a failure
    /// message cannot break that line structure. An event's data is multi-line by design and an exception message
    /// often is, so both pass through here wherever a message quotes them.</summary>
    /// <param name="value">The text to render.</param>
    /// <returns><paramref name="value"/> itself when it holds no line break; otherwise the escaped text.</returns>
    internal static string EscapeLineBreaks(string value)
        => value.AsSpan().IndexOfAny('\r', '\n') < 0
            ? value
            : value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}
