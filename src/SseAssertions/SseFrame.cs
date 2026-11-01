namespace SseAssertions;

/// <summary>One frame read by <see cref="SseStreamParser"/>.</summary>
/// <param name="Event">The frame; its <see cref="SseEvent.Data"/> is empty when it had no data.</param>
/// <param name="HadData">Whether the frame carried a <c>data:</c> line, so it is dispatched as an event.</param>
internal readonly record struct SseFrame(SseEvent Event, bool HadData);
