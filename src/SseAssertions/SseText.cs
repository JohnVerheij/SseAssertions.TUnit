using System;
using System.Globalization;

namespace SseAssertions;

/// <summary>Text helpers shared by both packages: the line-break escape of the failure messages and the
/// <c>retry:</c> value rule (this file is also compiled into <c>SseAssertions.TUnit</c>).</summary>
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

    /// <summary>Parses a <c>retry:</c> field value. The specification takes the value only when it consists of
    /// ASCII digits; every character is checked first, because <see cref="int.TryParse(ReadOnlySpan{char}, NumberStyles, IFormatProvider, out int)"/>
    /// also accepts trailing NUL characters. A value too large for an <see cref="int"/> is ignored too.</summary>
    /// <param name="value">The field value, after the optional single space.</param>
    /// <param name="parsed">The milliseconds when the value is valid; otherwise 0.</param>
    /// <returns><see langword="true"/> when the value is a valid reconnection time.</returns>
    internal static bool TryParseRetry(ReadOnlySpan<char> value, out int parsed)
    {
        // An empty value passes the loop, and int.TryParse rejects it.
        parsed = 0;
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
