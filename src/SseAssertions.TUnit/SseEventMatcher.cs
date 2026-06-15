using System;
using System.Collections.Generic;
using SseAssertions;

namespace SseAssertions.TUnit;

/// <summary>
/// Framework-neutral matching engine shared by every <c>HasSseEvent</c> chain (the
/// <see cref="string"/>, <see cref="System.IO.Stream"/>, and
/// <see cref="System.Net.Http.HttpResponseMessage"/> receivers). Holds the configured event-name
/// filter, the optional frame narrowers (data / parsed-data / id / retry) and the count comparison,
/// and evaluates a parsed event list into either a pass or a failure-message string.
/// </summary>
/// <remarks>
/// Returning <see langword="null"/> for a pass (and a <see cref="SseFailureMessage"/>-composed
/// string for a failure) keeps the matching logic free of any TUnit dependency. An internal helper:
/// the chains are its only constructors and they populate the parsed-data narrower through
/// <see cref="SseDataNarrow"/>, which always supplies a non-null exception when a parse throws, so
/// the matching code relies on that invariant. The narrower fields are mutated by the fluent chain's
/// <c>WithData</c>/<c>WithDataParsedAs</c>/<c>WithId</c>/<c>WithRetryMillis</c> methods and the
/// comparison by <c>AtLeast</c>/<c>AtMost</c>/<c>Exactly</c>.
/// </remarks>
internal sealed class SseEventMatcher
{
    private readonly string _eventName;

    /// <summary>Initialises the matcher for the given event-type name.</summary>
    /// <param name="eventName">The SSE event-type name to look for. Matched case-sensitively
    /// against <see cref="SseEvent.EventName"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    public SseEventMatcher(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        _eventName = eventName;
    }

    /// <summary>Gets or sets the data predicate; frames whose <see cref="SseEvent.Data"/> fails it
    /// do not count.</summary>
    public Func<string, bool>? DataPredicate { get; set; }

    /// <summary>Gets or sets the parsed-data narrower. Returns a tuple of (threw, exception,
    /// matched): when <c>threw</c> is set the assertion fails naming the deserializer exception,
    /// otherwise frames for which <c>matched</c> is <see langword="false"/> do not count.</summary>
    public Func<string, (bool Threw, Exception? Exception, bool Matched)>? ParsedNarrow { get; set; }

    /// <summary>Gets or sets whether an <c>id:</c> filter is active.</summary>
    public bool HasIdFilter { get; set; }

    /// <summary>Gets or sets the expected <see cref="SseEvent.Id"/> when <see cref="HasIdFilter"/>
    /// is set.</summary>
    public string? ExpectedId { get; set; }

    /// <summary>Gets or sets the retry predicate; frames whose <see cref="SseEvent.RetryMillis"/>
    /// fails it do not count.</summary>
    public Func<int?, bool>? RetryPredicate { get; set; }

    /// <summary>Gets or sets the count comparison applied to the match count.</summary>
    public SseCountComparison Comparison { get; set; } = SseCountComparison.AtLeast;

    /// <summary>Gets or sets the expected match count the comparison is applied against.</summary>
    public int ExpectedCount { get; set; } = 1;

    /// <summary>Counts the frames matching the event name and every configured narrower, then
    /// returns <see langword="null"/> on a pass or the most specific failure diagnostic.</summary>
    /// <param name="events">The parsed events from the receiver.</param>
    /// <returns><see langword="null"/> when the expectation is met; otherwise the failure
    /// message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is
    /// <see langword="null"/>.</exception>
    public string? Evaluate(IReadOnlyList<SseEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var matchCount = 0;
        var dataNearMisses = new List<string>();
        var idNearMisses = new List<string?>();
        var retryNearMisses = new List<int?>();

        foreach (var evt in events)
        {
            if (!string.Equals(evt.EventName, _eventName, StringComparison.Ordinal))
            {
                continue;
            }

            if (ParsedNarrow is not null)
            {
                var (threw, exception, matched) = ParsedNarrow(evt.Data);
                if (threw)
                {
                    return SseFailureMessage.DataDeserializationFailed(_eventName, evt.Data, exception!);
                }

                if (!matched)
                {
                    dataNearMisses.Add(evt.Data);
                    continue;
                }
            }

            if (DataPredicate is not null && !DataPredicate(evt.Data))
            {
                dataNearMisses.Add(evt.Data);
                continue;
            }

            if (HasIdFilter && !string.Equals(evt.Id, ExpectedId, StringComparison.Ordinal))
            {
                idNearMisses.Add(evt.Id);
                continue;
            }

            if (RetryPredicate is not null && !RetryPredicate(evt.RetryMillis))
            {
                retryNearMisses.Add(evt.RetryMillis);
                continue;
            }

            matchCount++;
        }

        return CountSatisfiesComparison(matchCount)
            ? null
            : DescribeFailure(events, matchCount, dataNearMisses, idNearMisses, retryNearMisses);
    }

    /// <summary>Selects the most specific failure message for an unmet expectation: event-absent,
    /// then per-narrower near-miss diagnostics, falling back to a count mismatch.</summary>
    /// <param name="events">The parsed events (for the event-absent listing).</param>
    /// <param name="matchCount">The number of fully matching frames.</param>
    /// <param name="dataNearMisses">Data values of frames that matched the name but failed a data
    /// narrower.</param>
    /// <param name="idNearMisses">Id values of frames that matched up to the id narrower.</param>
    /// <param name="retryNearMisses">Retry values of frames that matched up to the retry
    /// narrower.</param>
    /// <returns>The failure message.</returns>
    private string DescribeFailure(
        IReadOnlyList<SseEvent> events,
        int matchCount,
        List<string> dataNearMisses,
        List<string?> idNearMisses,
        List<int?> retryNearMisses)
    {
        // An event of the requested type was observed when it either fully matched or failed a
        // narrower (a near-miss). With none of either, the event is genuinely absent, whether or not a
        // narrower is configured, so the event-absent diagnostic takes precedence over a count mismatch.
        var nameMatched = matchCount + dataNearMisses.Count + idNearMisses.Count + retryNearMisses.Count;
        if (nameMatched is 0)
        {
            return SseFailureMessage.EventNotFound(_eventName, events);
        }

        if (matchCount is 0 && dataNearMisses.Count > 0)
        {
            return SseFailureMessage.DataPredicateNotMatched(_eventName, dataNearMisses);
        }

        if (matchCount is 0 && idNearMisses.Count > 0)
        {
            return SseFailureMessage.IdNotMatched(_eventName, ExpectedId!, idNearMisses);
        }

        if (matchCount is 0 && retryNearMisses.Count > 0)
        {
            return SseFailureMessage.RetryMillisPredicateNotMatched(_eventName, retryNearMisses);
        }

        return SseFailureMessage.EventCountMismatch(_eventName, ExpectedCount, matchCount, Comparison);
    }

    private bool CountSatisfiesComparison(int actual)
        => Comparison switch
        {
            SseCountComparison.AtLeast => actual >= ExpectedCount,
            SseCountComparison.AtMost => actual <= ExpectedCount,
            _ => actual == ExpectedCount, // SseCountComparison.Exactly
        };
}
