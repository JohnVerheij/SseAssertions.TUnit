using System.Globalization;
using System.Text;
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
    /// <summary>Renders the expectation line for a matcher: the count comparison against the event-type name,
    /// then each narrower and the end of the observation window that the chain configured.</summary>
    /// <param name="eventName">The SSE event-type name.</param>
    /// <param name="matcher">The matcher that holds the comparison, the narrowers and the window.</param>
    /// <returns>The expectation line.</returns>
    public static string Describe(string eventName, SseEventMatcher matcher)
    {
        var label = matcher.Comparison switch
        {
            SseCountComparison.AtLeast => "at least",
            SseCountComparison.AtMost => "at most",
            _ => "exactly", // SseCountComparison.Exactly
        };

        var sb = new StringBuilder("to find ")
            .Append(label)
            .Append(' ')
            .Append(matcher.ExpectedCount.ToString(CultureInfo.InvariantCulture))
            .Append(" event(s) of type \"")
            .Append(eventName)
            .Append('"');
        AppendNarrowers(sb, matcher);
        if (matcher.Until is { } until)
        {
            sb.Append(", observed until an event of type \"").Append(until).Append('"');
        }

        return sb.ToString();
    }

    /// <summary>Appends a phrase for each narrower the chain configured.</summary>
    /// <param name="sb">The expectation line being built.</param>
    /// <param name="matcher">The matcher that holds the narrowers.</param>
    private static void AppendNarrowers(StringBuilder sb, SseEventMatcher matcher)
    {
        if (matcher.DataPredicate is not null || matcher.ParsedNarrow is not null)
        {
            sb.Append(" whose Data satisfies the predicate");
        }

        if (matcher.HasIdFilter)
        {
            sb.Append(" with id \"").Append(matcher.ExpectedId).Append('"');
        }
        else if (matcher.IdPredicate is not null)
        {
            sb.Append(" whose id satisfies the predicate");
        }

        if (matcher.RetryPredicate is not null)
        {
            sb.Append(" whose RetryMillis satisfies the predicate");
        }
    }
}
