using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SseAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// TUnit-native fluent entry points for asserting on Server-Sent Events streams represented as
/// text. v0.0.1 ships the <c>IsServerSentEventsStream()</c> discriminator only; the full surface
/// (HTTP response bodies, streams, per-frame parsing, event-name / id / data assertions) ships
/// in v0.1.0 once the wire-format parser is in place.
/// </summary>
/// <remarks>
/// Source methods carry the <c>[GenerateAssertion]</c> attribute; TUnit's source generator emits
/// the fluent <c>Assert.That(...).&lt;Method&gt;()</c> entry point at consumer build time. The
/// generated chain is AOT-clean (no runtime reflection in the assertion path).
/// </remarks>
public static class SseFormatAssertions
{
    /// <summary>
    /// Asserts that the supplied text has the shape of a Server-Sent Events stream: contains at
    /// least one SSE field marker (<c>event:</c>, <c>data:</c>, <c>id:</c>, or <c>retry:</c>) and
    /// the frame-separator double newline. The check is intentionally lightweight; structured
    /// per-frame assertions ship in v0.1.0.
    /// </summary>
    /// <param name="body">The text to inspect, as the receiver of the fluent assertion.</param>
    /// <returns>A passing assertion when the text looks like an SSE stream; otherwise a failing
    /// assertion identifying the shape mismatch.</returns>
    [GenerateAssertion]
    public static AssertionResult IsServerSentEventsStream(this string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return SseFormat.LooksLikeServerSentEvents(body)
            ? AssertionResult.Passed
            : AssertionResult.Failed(
                "the value to have the shape of a Server-Sent Events stream\n"
                + "  but no SSE field marker (event:, data:, id:, retry:) was found before a frame separator (\\n\\n)\n");
    }

    /// <summary>Asserts the first SSE frame parsed from <paramref name="body"/> has
    /// <c>event:</c> equal to <paramref name="eventName"/>. Unlabelled frames match
    /// <c>HasFirstSseEvent("message")</c> per the WHATWG default-event-name rule.</summary>
    /// <param name="body">The SSE wire-format body to inspect.</param>
    /// <param name="eventName">The event-type name expected on the first frame.</param>
    /// <returns>A passing assertion when the first parsed frame matches; otherwise a failing
    /// assertion naming the observed first event or reporting "no events".</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> or
    /// <paramref name="eventName"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static AssertionResult HasFirstSseEvent(this string body, string eventName)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(eventName);

        var events = SseFrameParser.Parse(body);
        return EvaluateFirstEvent(events, eventName);
    }

    internal static AssertionResult EvaluateFirstEvent(IReadOnlyList<SseEvent> events, string expectedEventName)
    {
        if (events.Count is 0)
        {
            return AssertionResult.Failed(string.Concat(
                "the first event to be \"",
                expectedEventName,
                "\"\n  but the stream contained no events"));
        }

        var actualFirst = events[0].EventName;
        return string.Equals(actualFirst, expectedEventName, StringComparison.Ordinal)
            ? AssertionResult.Passed
            : AssertionResult.Failed(string.Concat(
                "the first event to be \"",
                expectedEventName,
                "\"\n  but the first event was \"",
                actualFirst,
                "\""));
    }

    /// <summary>Asserts the SSE stream parsed from <paramref name="body"/> contains a
    /// <c>retry:</c> directive. When <paramref name="millis"/> is supplied, requires at least one
    /// frame whose <c>retry:</c> value equals it; when <see langword="null"/>, any non-null
    /// <c>retry:</c> value passes.</summary>
    /// <param name="body">The SSE wire-format body to inspect.</param>
    /// <param name="millis">The required <c>retry:</c> value in milliseconds, or
    /// <see langword="null"/> to accept any value.</param>
    /// <returns>A passing assertion when a matching <c>retry:</c> directive is found; otherwise a
    /// failing assertion describing what was observed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static AssertionResult HasSseRetryDirective(this string body, int? millis = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        var events = SseFrameParser.Parse(body);
        return EvaluateRetryDirective(events, millis);
    }

    internal static AssertionResult EvaluateRetryDirective(IReadOnlyList<SseEvent> events, int? expectedMillis)
    {
        var observedValues = new List<int>();
        for (var i = 0; i < events.Count; i++)
        {
            var retry = events[i].RetryMillis;
            if (retry is null)
            {
                continue;
            }

            observedValues.Add(retry.Value);
            if (expectedMillis is null || retry.Value == expectedMillis.Value)
            {
                return AssertionResult.Passed;
            }
        }

        if (expectedMillis is null)
        {
            return AssertionResult.Failed(
                "the stream to contain a \"retry:\" directive\n  but no frame carried a retry value");
        }

        if (observedValues.Count is 0)
        {
            return AssertionResult.Failed(string.Concat(
                "the stream to contain a \"retry: ",
                expectedMillis.Value.ToString(CultureInfo.InvariantCulture),
                "\" directive\n  but no frame carried a retry value"));
        }

        var sb = new StringBuilder();
        sb.Append("the stream to contain a \"retry: ")
          .Append(expectedMillis.Value.ToString(CultureInfo.InvariantCulture))
          .Append("\" directive\n  but observed retry value(s): ");
        for (var i = 0; i < observedValues.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(observedValues[i].ToString(CultureInfo.InvariantCulture));
        }
        return AssertionResult.Failed(sb.ToString());
    }

    /// <summary>Asserts the SSE stream parsed from <paramref name="body"/> sends a <c>retry:</c>
    /// directive before any data-bearing event. A <c>retry:</c> directive is a reconnection-time
    /// hint, not a named event, so this is distinct from <c>HasFirstSseEvent</c>: the contract is
    /// "the server set the reconnection time before streaming any payload". An empty <c>data:</c>
    /// line carries no payload and does not count as data: the standard ASP.NET Core SSE serializer
    /// emits a reconnection control frame as <c>event: retry</c> + an empty <c>data:</c> line +
    /// <c>retry:</c>, and that empty line must not be read as data preceding the directive.</summary>
    /// <param name="body">The SSE wire-format body to inspect.</param>
    /// <returns>A passing assertion when a <c>retry:</c> directive precedes the first data event;
    /// otherwise a failing assertion (no retry directive at all, or a data event came first).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static AssertionResult HasSseRetryDirectiveFirst(this string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return EvaluateRetryDirectiveFirst(body);
    }

    /// <summary>Asserts the SSE stream parsed from <paramref name="body"/> sends a <c>retry:</c>
    /// directive before any data-bearing event <em>and</em> that the leading directive's value equals
    /// <paramref name="millis"/>. The value-pinning companion to <see cref="HasSseRetryDirectiveFirst(string)"/>:
    /// asserts both position and value in a single pass, where an empty <c>data:</c> control-frame line
    /// does not count as data (see <see cref="HasSseRetryDirectiveFirst(string)"/>).</summary>
    /// <param name="body">The SSE wire-format body to inspect.</param>
    /// <param name="millis">The required <c>retry:</c> value, in milliseconds, of the leading directive.</param>
    /// <returns>A passing assertion when a <c>retry: millis</c> directive precedes the first data event;
    /// otherwise a failing assertion (no retry directive, a data event came first, or the leading
    /// directive carried a different value).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    [GenerateAssertion]
    public static AssertionResult HasSseRetryDirectiveFirst(this string body, int millis)
    {
        ArgumentNullException.ThrowIfNull(body);
        return EvaluateRetryDirectiveFirst(body, millis);
    }

    internal static AssertionResult EvaluateRetryDirectiveFirst(string body, int? expectedMillis = null)
    {
        // Strip a single leading UTF-8 BOM (U+FEFF) the same way SseFrameParser.Parse does, so a
        // stream that opens with a BOM does not mask the first `retry:` field from the wire-level
        // scan below (without this, lines[0] starts with the BOM and IsField(.., "retry") misses it).
        if (body.Length > 0 && body[0] is '\uFEFF')
        {
            body = body[1..];
        }

        // Wire-level check: the first `retry:` field line must precede the first data-bearing line.
        // A `data:` line with an empty value carries no payload and does not count as "data first":
        // the standard ASP.NET Core SSE serializer writes a reconnection control frame as
        // `event: retry` + an empty `data:` line + `retry: <ms>` (the BCL fixes field order to
        // event/data/id/retry), so the empty `data:` precedes `retry:` on the wire even though the
        // directive is the first dispatched event. Only a non-empty `data:` value before the first
        // `retry:` fails. The order is read from the raw field lines rather than the parsed event
        // list because a retry-only frame carries no data and is dropped by the WHATWG parser. Line
        // endings normalize to LF first so \r\n / \r / \n split identically.
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var firstRetry = -1;
        var firstData = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (firstRetry < 0 && IsField(lines[i], "retry"))
            {
                firstRetry = i;
            }

            if (firstData < 0 && IsNonEmptyDataField(lines[i]))
            {
                firstData = i;
            }

            if (firstRetry >= 0 && firstData >= 0)
            {
                // Both first-occurrences located; later lines cannot change the outcome.
                break;
            }
        }

        if (firstRetry < 0)
        {
            return AssertionResult.Failed(
                "the stream to send a retry directive before any data\n  but no retry directive was found");
        }

        if (firstData >= 0 && firstData < firstRetry)
        {
            return AssertionResult.Failed(
                "the stream to send a retry directive before any data\n  but a data field appeared before the first retry directive");
        }

        if (expectedMillis is int expected
            && CheckLeadingRetryValue(lines[firstRetry], expected) is { } mismatch)
        {
            return mismatch;
        }

        return AssertionResult.Passed;
    }

    /// <summary>Verifies the leading <c>retry:</c> directive's value equals <paramref name="expected"/>,
    /// returning a failing <see cref="AssertionResult"/> when it does not (or is unparseable), or
    /// <see langword="null"/> when it matches.</summary>
    /// <param name="retryLine">The leading <c>retry:</c> field line.</param>
    /// <param name="expected">The required <c>retry:</c> value in milliseconds.</param>
    /// <returns>A failing assertion on mismatch; otherwise <see langword="null"/>.</returns>
    private static AssertionResult? CheckLeadingRetryValue(string retryLine, int expected)
    {
        var actual = ParseRetryValue(retryLine);
        if (actual == expected)
        {
            return null;
        }

        return AssertionResult.Failed(string.Concat(
            "the stream to send a \"retry: ",
            expected.ToString(CultureInfo.InvariantCulture),
            "\" directive before any data\n  but the leading retry directive was ",
            actual is null
                ? "unparseable"
                : string.Concat("\"retry: ", actual.Value.ToString(CultureInfo.InvariantCulture), "\"")));
    }

    /// <summary>Parses the integer millisecond value from a <c>retry:</c> field line. Returns
    /// <see langword="null"/> when the value is absent or not all-ASCII-digits (per the SSE spec a
    /// non-numeric <c>retry:</c> value is ignored). <paramref name="line"/> is assumed to be a
    /// <c>retry</c> field line as confirmed by <see cref="IsField"/>.</summary>
    /// <param name="line">The <c>retry:</c> field line.</param>
    /// <returns>The parsed millisecond value, or <see langword="null"/> when not a valid integer.</returns>
    private static int? ParseRetryValue(ReadOnlySpan<char> line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        var value = line[(colon + 1)..];
        if (value.Length > 0 && value[0] is ' ')
        {
            value = value[1..];
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) ? ms : null;
    }

    /// <summary>Reports whether <paramref name="line"/> is a <c>data:</c> field line carrying a
    /// non-empty value. A bare <c>data</c> line (no colon) or a <c>data:</c> line whose value is
    /// empty after stripping the single optional leading space carries no payload and returns
    /// <see langword="false"/>: the standard SSE serializer emits such an empty <c>data:</c> line
    /// alongside a <c>retry:</c> control frame, and it must not count as data preceding the retry
    /// directive.</summary>
    private static bool IsNonEmptyDataField(ReadOnlySpan<char> line)
    {
        if (line.Length is 0 || line[0] is ':')
        {
            return false;
        }

        var colon = line.IndexOf(':');
        if (colon < 0)
        {
            // Bare "data" is a data field with an empty value per spec.
            return false;
        }

        if (line[..colon] is not "data")
        {
            return false;
        }

        var value = line[(colon + 1)..];
        if (value.Length > 0 && value[0] is ' ')
        {
            value = value[1..];
        }

        return value.Length > 0;
    }

    /// <summary>Reports whether <paramref name="line"/> is an SSE field line for
    /// <paramref name="fieldName"/> (its field name, the text before the first colon, equals
    /// <paramref name="fieldName"/>). Comment lines (leading <c>:</c>) and blank lines are not
    /// fields.</summary>
    private static bool IsField(ReadOnlySpan<char> line, ReadOnlySpan<char> fieldName)
    {
        if (line.Length is 0 || line[0] is ':')
        {
            return false;
        }

        var colon = line.IndexOf(':');
        var name = colon < 0 ? line : line[..colon];
        return name.SequenceEqual(fieldName);
    }
}
