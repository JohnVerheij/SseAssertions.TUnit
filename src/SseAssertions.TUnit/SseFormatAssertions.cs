using System;
using System.Collections.Generic;
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
}
