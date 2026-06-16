using System;

namespace SseAssertions.TUnit;

/// <summary>
/// Builds the parsed-data narrowing delegate shared by every <c>HasSseEvent</c> chain. Centralises
/// the deserialize-then-test step (and the single broad exception handler that turns a deserializer
/// throw into a <c>DataDeserializationFailed</c> diagnostic) so the <see cref="string"/>,
/// <see cref="System.IO.Stream"/>, and <see cref="System.Net.Http.HttpResponseMessage"/> chains
/// stay byte-for-byte identical in behaviour.
/// </summary>
internal static class SseDataNarrow
{
    /// <summary>Wraps a parser and predicate into the tuple-returning narrower the
    /// <see cref="SseEventMatcher"/> consumes.</summary>
    /// <typeparam name="T">The type the frame data is parsed into.</typeparam>
    /// <param name="parse">The deserializer applied to a frame's data.</param>
    /// <param name="predicate">The predicate applied to the parsed value.</param>
    /// <returns>A delegate returning (threw, exception, matched) for each frame's data.</returns>
    public static Func<string, (bool Threw, Exception? Exception, bool Matched)> Build<T>(
        Func<string, T> parse, Func<T, bool> predicate)
        => data =>
        {
            T parsed;
            try
            {
                parsed = parse(data);
            }
#pragma warning disable CA1031 // Any deserializer exception is surfaced as a DataDeserializationFailed diagnostic.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return (true, ex, false);
            }

            return (false, null, predicate(parsed));
        };
}
