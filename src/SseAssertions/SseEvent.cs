namespace SseAssertions;

/// <summary>
/// A single Server-Sent Events frame parsed from a stream. Matches the field set defined by the
/// WHATWG / W3C SSE specification: the event-name dispatched (<c>event:</c>), the last-event-id
/// (<c>id:</c>), the reconnection time the server suggests (<c>retry:</c>), and the buffered
/// data payload (<c>data:</c> lines joined by <c>'\n'</c>).
/// </summary>
/// <remarks>
/// <para>
/// The record is the stable public data type for the assertion family. Adapters parse a raw SSE
/// stream into <see cref="SseEvent"/> instances and the fluent assertion entry points (shipped
/// in <c>SseAssertions.TUnit</c>) consume them.
/// </para>
/// <para>
/// The frame parser itself ships in a later release; v0.0.1 is the skeleton release establishing
/// the public surface seam, repository, and package identifiers on nuget.org.
/// </para>
/// </remarks>
/// <param name="EventName">The event type the server dispatched via the <c>event:</c> field, or
/// <see langword="null"/> when the frame carries no event name (the consumer should treat such a
/// frame as the default <c>message</c> event per the SSE specification).</param>
/// <param name="Id">The last-event-id the server set via the <c>id:</c> field, or
/// <see langword="null"/> when no id was emitted.</param>
/// <param name="RetryMillis">The reconnection time in milliseconds the server suggested via the
/// <c>retry:</c> field, or <see langword="null"/> when no retry was emitted.</param>
/// <param name="Data">The data payload, with multiple <c>data:</c> lines joined by a single
/// <c>'\n'</c> character. Never <see langword="null"/>; an empty string indicates a frame whose
/// <c>data:</c> field was empty or absent.</param>
public sealed record SseEvent(
    string? EventName,
    string? Id,
    int? RetryMillis,
    string Data);
