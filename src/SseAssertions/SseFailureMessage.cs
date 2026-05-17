using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SseAssertions;

/// <summary>
/// Curated factory methods for SSE assertion failure messages. Exposed as the v0.1.0+ extension
/// point for consumer-authored typed SSE assertions: a consumer who writes their own
/// <c>[GenerateAssertion]</c>-tagged extension can compose these factories to produce failure
/// messages whose shape matches the shipped chain terminators.
/// </summary>
/// <remarks>
/// <para>
/// All factories return culture-invariant strings using <see cref="CultureInfo.InvariantCulture"/>
/// for any numeric formatting. Truncation rules are consistent across factories:
/// per-event <c>Data</c> fields are truncated at 80 characters with a trailing <c>…</c>
/// (U+2026 HORIZONTAL ELLIPSIS); body-context excerpts in <see cref="ParseFailure(string)"/> and
/// the <c>partialBodyExcerpt</c> argument of
/// <see cref="CancellationCutRead(int, int, string)"/> are truncated at 256 characters with the
/// same suffix.
/// </para>
/// <para>
/// The factories mirror the
/// <c>JsonAssertions.JsonFailureMessage</c> and <c>MathAssertions.MathFailureMessage</c> patterns
/// in sibling family packages.
/// </para>
/// </remarks>
public static class SseFailureMessage
{
    private const int DataTruncationLimit = 80;
    private const int BodyTruncationLimit = 256;
    private const string TruncationSuffix = "…";

    /// <summary>Produces the failure message for an SSE body that could not be parsed.</summary>
    /// <param name="body">The body that failed to parse. Embedded in the message, truncated at
    /// <c>256</c> characters with a trailing <c>…</c>.</param>
    /// <returns>A failure message describing the parse failure with the truncated body excerpt.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    public static string ParseFailure(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return string.Concat(
            "the value to parse as a Server-Sent Events stream\n  but the body could not be parsed as SSE wire format\n  body: ",
            TruncateBody(body));
    }

    /// <summary>Produces the failure message for "no event of the requested name was found".</summary>
    /// <param name="eventName">The event-type name the chain asked for.</param>
    /// <param name="available">The full list of events observed in the stream (rendered for
    /// diagnosis; per-event <c>Data</c> truncated at <c>80</c> characters).</param>
    /// <returns>A failure message listing every observed event so the consumer can see why the
    /// requested event was not found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> or
    /// <paramref name="available"/> is <see langword="null"/>.</exception>
    public static string EventNotFound(string eventName, IReadOnlyList<SseEvent> available)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(available);

        var sb = new StringBuilder();
        sb.Append("to find at least one event of type \"")
          .Append(eventName)
          .Append("\"\n  but observed: 0 events of type \"")
          .Append(eventName)
          .Append("\" out of ")
          .Append(available.Count.ToString(CultureInfo.InvariantCulture))
          .Append(" total event(s) captured");
        AppendEventList(sb, available);
        return sb.ToString();
    }

    /// <summary>Produces the failure message for an event-count terminator failure.</summary>
    /// <param name="eventName">The event-type name the chain asked for.</param>
    /// <param name="expected">The expected count (the value passed to the terminator).</param>
    /// <param name="actual">The actual matching-event count observed.</param>
    /// <param name="comparison">The comparison label the chain used.</param>
    /// <returns>A failure message describing the count mismatch.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    public static string EventCountMismatch(string eventName, int expected, int actual, SseCountComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(eventName);

        var label = comparison switch
        {
            SseCountComparison.AtLeast => "at least",
            SseCountComparison.AtMost => "at most",
            SseCountComparison.Exactly => "exactly",
            _ => "exactly",
        };

        return string.Concat(
            "to find ",
            label,
            " ",
            expected.ToString(CultureInfo.InvariantCulture),
            " event(s) of type \"",
            eventName,
            "\"\n  but observed: ",
            actual.ToString(CultureInfo.InvariantCulture),
            " event(s) of type \"",
            eventName,
            "\"");
    }

    /// <summary>Produces the failure message for "data predicate did not match any event".</summary>
    /// <param name="eventName">The event-type name the chain asked for.</param>
    /// <param name="dataValues">The <c>Data</c> values of every event of the requested type
    /// observed in the stream (each rendered truncated at <c>80</c> characters).</param>
    /// <returns>A failure message listing every observed <c>Data</c> value so the consumer can
    /// see why the predicate did not match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> or
    /// <paramref name="dataValues"/> is <see langword="null"/>.</exception>
    public static string DataPredicateNotMatched(string eventName, IReadOnlyList<string> dataValues)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(dataValues);

        var sb = new StringBuilder();
        sb.Append("to find at least one event of type \"")
          .Append(eventName)
          .Append("\" whose Data satisfied the predicate\n  but observed ")
          .Append(dataValues.Count.ToString(CultureInfo.InvariantCulture))
          .Append(" event(s) of that type; none matched");
        if (dataValues.Count > 0)
        {
            sb.Append(":\n");
            for (var i = 0; i < dataValues.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }

                sb.Append("    data: \"").Append(TruncateData(dataValues[i])).Append('"');
            }
        }

        return sb.ToString();
    }

    /// <summary>Produces the failure message for an exception thrown by the data deserializer.</summary>
    /// <param name="eventName">The event-type name the chain asked for.</param>
    /// <param name="data">The raw <c>Data</c> value the deserializer was applied to (truncated at
    /// <c>80</c> characters).</param>
    /// <param name="inner">The exception the deserializer threw.</param>
    /// <returns>A failure message including the exception type, message, and the offending
    /// (truncated) data value.</returns>
    /// <exception cref="ArgumentNullException">Any of <paramref name="eventName"/>,
    /// <paramref name="data"/>, or <paramref name="inner"/> is <see langword="null"/>.</exception>
    public static string DataDeserializationFailed(string eventName, string data, Exception inner)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(inner);

        return string.Concat(
            "to deserialize Data for event \"",
            eventName,
            "\"\n  but the deserializer threw ",
            inner.GetType().Name,
            ": ",
            inner.Message,
            "\n  data: \"",
            TruncateData(data),
            "\"");
    }

    /// <summary>Produces the failure message for a retry-millis predicate failure.</summary>
    /// <param name="eventName">The event-type name the chain asked for.</param>
    /// <param name="retryValues">The <c>RetryMillis</c> values observed on every event of the
    /// requested type (<see langword="null"/> entries represent frames without a
    /// <c>retry:</c> directive).</param>
    /// <returns>A failure message listing every observed retry value so the consumer can see why
    /// the predicate did not match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> or
    /// <paramref name="retryValues"/> is <see langword="null"/>.</exception>
    public static string RetryMillisPredicateNotMatched(string eventName, IReadOnlyList<int?> retryValues)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(retryValues);

        var sb = new StringBuilder();
        sb.Append("to find at least one event of type \"")
          .Append(eventName)
          .Append("\" whose RetryMillis satisfied the predicate\n  but observed ")
          .Append(retryValues.Count.ToString(CultureInfo.InvariantCulture))
          .Append(" event(s) of that type; none matched");
        if (retryValues.Count > 0)
        {
            sb.Append(":\n");
            for (var i = 0; i < retryValues.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }

                sb.Append("    retry: ");
                var value = retryValues[i];
                sb.Append(value is null ? "<absent>" : value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    /// <summary>Produces the failure message for an HTTP response whose <c>Content-Type</c> was
    /// not <c>text/event-stream</c>.</summary>
    /// <param name="actualContentType">The actual <c>Content-Type</c> media type observed on the
    /// response, or <see langword="null"/> when no <c>Content-Type</c> header was present.</param>
    /// <returns>A failure message naming the expected SSE content type and the actual response
    /// content type.</returns>
    public static string UnexpectedContentType(string? actualContentType)
    {
        return string.Concat(
            "the response to have Content-Type \"text/event-stream\"\n  but got: ",
            actualContentType ?? "<absent>");
    }

    /// <summary>Produces the failure message for a cancelled read whose partial buffer did not
    /// satisfy the chain.</summary>
    /// <param name="bytesReceived">The number of bytes received before cancellation fired.</param>
    /// <param name="eventsParsed">The number of fully-terminated events the parser recovered from
    /// the partial buffer.</param>
    /// <param name="partialBodyExcerpt">The first <c>256</c> characters of the partial buffer
    /// (truncated with a trailing <c>…</c> when longer).</param>
    /// <returns>A failure message describing the cancellation-bounded read result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partialBodyExcerpt"/> is
    /// <see langword="null"/>.</exception>
    public static string CancellationCutRead(int bytesReceived, int eventsParsed, string partialBodyExcerpt)
    {
        ArgumentNullException.ThrowIfNull(partialBodyExcerpt);

        return string.Concat(
            "the read was cancelled after ",
            bytesReceived.ToString(CultureInfo.InvariantCulture),
            " byte(s); parsed ",
            eventsParsed.ToString(CultureInfo.InvariantCulture),
            " event(s) from the partial buffer\n  partial body excerpt: ",
            TruncateBody(partialBodyExcerpt));
    }

    private static string TruncateData(string data)
        => data.Length <= DataTruncationLimit ? data : string.Concat(data.AsSpan(0, DataTruncationLimit), TruncationSuffix);

    private static string TruncateBody(string body)
        => body.Length <= BodyTruncationLimit ? body : string.Concat(body.AsSpan(0, BodyTruncationLimit), TruncationSuffix);

    private static void AppendEventList(StringBuilder sb, IReadOnlyList<SseEvent> events)
    {
        if (events.Count is 0)
        {
            return;
        }

        sb.Append(':');
        for (var i = 0; i < events.Count; i++)
        {
            sb.Append("\n    [")
              .Append(i.ToString(CultureInfo.InvariantCulture))
              .Append("] event=")
              .Append(events[i].EventName)
              .Append(" data=\"")
              .Append(TruncateData(events[i].Data))
              .Append('"');
        }
    }
}
