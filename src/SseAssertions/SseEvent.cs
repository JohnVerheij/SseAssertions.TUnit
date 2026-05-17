namespace SseAssertions;

/// <summary>
/// A single Server-Sent Events frame parsed from a stream. Matches the field set defined by the
/// WHATWG / W3C SSE specification: the event-name dispatched (<c>event:</c>), the buffered data
/// payload (<c>data:</c> lines joined by <c>'\n'</c>), the last-event-id (<c>id:</c>), and the
/// reconnection time the server suggests (<c>retry:</c>).
/// </summary>
/// <remarks>
/// <para>
/// The record is the stable public data type for the assertion family. <see cref="SseFrameParser"/>
/// parses a raw SSE body string into <see cref="SseEvent"/> instances and the fluent assertion
/// entry points (shipped in <c>SseAssertions.TUnit</c>) consume them.
/// </para>
/// <para>
/// <see cref="EventName"/> is non-nullable. A frame without an explicit <c>event:</c> field
/// dispatches as the SSE-default <c>"message"</c> per the WHATWG specification; the parser fills
/// in that default so consumers never have to reason about the missing-event-name case.
/// </para>
/// </remarks>
/// <param name="EventName">The event type the frame dispatched. Equals the value of the
/// <c>event:</c> field when one was emitted, or the string <c>"message"</c> when no <c>event:</c>
/// field was present (the SSE-spec default).</param>
/// <param name="Data">The data payload, with multiple <c>data:</c> lines joined by a single
/// <c>'\n'</c> character. Never <see langword="null"/>; an empty string indicates a frame whose
/// <c>data:</c> field was empty or absent.</param>
/// <param name="Id">The last-event-id the server set via the <c>id:</c> field, or
/// <see langword="null"/> when no id was emitted.</param>
/// <param name="RetryMillis">The reconnection time in milliseconds the server suggested via the
/// <c>retry:</c> field, or <see langword="null"/> when no retry was emitted or the value did not
/// parse as a non-negative integer.</param>
public sealed record SseEvent(
    string EventName,
    string Data,
    string? Id = null,
    int? RetryMillis = null);
