using System.Globalization;
using SseAssertions;

namespace SseAssertions.TUnit;

/// <summary>
/// Builds the human-readable expectation line ("to find at least N event(s) of type ...") shared by
/// every <c>HasSseEvent</c> chain, so the <see cref="string"/>, <see cref="System.IO.Stream"/>, and
/// <see cref="System.Net.Http.HttpResponseMessage"/> receivers describe an unmet expectation
/// identically.
/// </summary>
internal static class SseExpectation
{
    /// <summary>Renders the expectation line for a count comparison against an event-type name.</summary>
    /// <param name="eventName">The SSE event-type name.</param>
    /// <param name="comparison">The count comparison.</param>
    /// <param name="expectedCount">The expected count the comparison is applied against.</param>
    /// <returns>The expectation line.</returns>
    public static string Describe(string eventName, SseCountComparison comparison, int expectedCount)
    {
        var label = comparison switch
        {
            SseCountComparison.AtLeast => "at least",
            SseCountComparison.AtMost => "at most",
            _ => "exactly", // SseCountComparison.Exactly
        };

        return string.Concat(
            "to find ",
            label,
            " ",
            expectedCount.ToString(CultureInfo.InvariantCulture),
            " event(s) of type \"",
            eventName,
            "\"");
    }
}
