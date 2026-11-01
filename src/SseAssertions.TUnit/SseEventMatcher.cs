using System;
using System.Collections.Generic;
using SseAssertions;

namespace SseAssertions.TUnit;

/// <summary>
/// Framework-neutral matching engine shared by every <c>HasSseEvent</c> chain (the
/// <see cref="string"/>, <see cref="System.IO.Stream"/>, and
/// <see cref="System.Net.Http.HttpResponseMessage"/> receivers). Holds the configured event-name
/// filter, the optional frame narrowers (data / parsed-data / id / retry), the count comparison, and the
/// event that ends the observation window, and evaluates events one at a time.
/// </summary>
/// <remarks>
/// <para>
/// Observing one event at a time lets a streaming read stop once the outcome is decided: <c>AtLeast(n)</c>
/// passes at the n-th match, <c>AtMost</c> and <c>Exactly</c> fail as soon as the count is exceeded, and a
/// parsed-data narrower that throws fails at once. Otherwise <see cref="Finish"/> decides when the window ends.
/// </para>
/// <para>
/// Returning <see langword="null"/> for a pass (and a <see cref="SseFailureMessage"/>-composed string for a
/// failure) keeps the matching logic free of any TUnit dependency. The chains populate the parsed-data narrower
/// through <see cref="SseDataNarrow"/>, which always supplies a non-null exception when a parse throws, so the
/// matching code relies on that invariant.
/// </para>
/// </remarks>
internal sealed class SseEventMatcher : ISseObserver
{
    private readonly string _eventName;
    private readonly List<SseEvent> _observed = [];
    private readonly List<string> _dataNearMisses = [];
    private readonly List<string?> _idNearMisses = [];
    private readonly List<int?> _retryNearMisses = [];
    private int _matchCount;

    /// <summary>Initializes the matcher for the given event-type name.</summary>
    /// <param name="eventName">The SSE event-type name to look for. Matched case-sensitively
    /// against <see cref="SseEvent.EventName"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    internal SseEventMatcher(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        _eventName = eventName;
    }

    /// <summary>Gets or sets the data predicate; frames whose <see cref="SseEvent.Data"/> fails it
    /// do not count.</summary>
    internal Func<string, bool>? DataPredicate { get; set; }

    /// <summary>Gets or sets the parsed-data narrower. Returns a tuple of (threw, exception,
    /// matched): when <c>threw</c> is set the assertion fails naming the deserializer exception,
    /// otherwise frames for which <c>matched</c> is <see langword="false"/> do not count.</summary>
    internal Func<string, (bool Threw, Exception? Exception, bool Matched)>? ParsedNarrow { get; set; }

    /// <summary>Gets or sets whether an exact <c>id:</c> filter is active.</summary>
    internal bool HasIdFilter { get; set; }

    /// <summary>Gets or sets the expected <see cref="SseEvent.Id"/> when <see cref="HasIdFilter"/>
    /// is set.</summary>
    internal string? ExpectedId { get; set; }

    /// <summary>Gets or sets the id predicate; frames whose <see cref="SseEvent.Id"/> fails it do not
    /// count.</summary>
    internal Func<string?, bool>? IdPredicate { get; set; }

    /// <summary>Gets or sets the retry predicate; frames whose <see cref="SseEvent.RetryMillis"/>
    /// fails it do not count.</summary>
    internal Func<int?, bool>? RetryPredicate { get; set; }

    /// <summary>Gets or sets the count comparison applied to the match count.</summary>
    internal SseCountComparison Comparison { get; set; } = SseCountComparison.AtLeast;

    /// <summary>Gets or sets the expected match count the comparison is applied against.</summary>
    internal int ExpectedCount { get; set; } = 1;

    /// <inheritdoc/>
    public string? Until { get; set; }

    /// <summary>Evaluates a whole parsed body: observes the events up to the end of the window, stopping once the
    /// outcome is decided, as a streaming read would.</summary>
    /// <param name="events">The parsed events from the receiver.</param>
    /// <returns><see langword="null"/> when the expectation is met; otherwise the failure message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is
    /// <see langword="null"/>.</exception>
    internal string? Evaluate(IReadOnlyList<SseEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return SseStreamEvaluation.EvaluateParsed(this, events);
    }

    /// <inheritdoc/>
    public SseOutcome? Observe(SseEvent evt)
    {
        _observed.Add(evt);
        if (!string.Equals(evt.EventName, _eventName, StringComparison.Ordinal))
        {
            return null;
        }

        if (ParsedNarrow is not null)
        {
            var (threw, exception, matched) = ParsedNarrow(evt.Data);
            if (threw)
            {
                return new SseOutcome(SseFailureMessage.DataDeserializationFailed(_eventName, evt.Data, exception!));
            }

            if (!matched)
            {
                _dataNearMisses.Add(evt.Data);
                return null;
            }
        }

        if (!PassesNarrowers(evt))
        {
            return null;
        }

        _matchCount++;
        return DecideCount();
    }

    /// <summary>Decides the outcome once a match was counted: a pass at the n-th match of <c>AtLeast(n)</c>, a
    /// failure once <c>AtMost</c> or <c>Exactly</c> is exceeded.</summary>
    /// <returns>The decided outcome, or <see langword="null"/> while it is not decided.</returns>
    private SseOutcome? DecideCount()
    {
        if (Comparison is SseCountComparison.AtLeast)
        {
            return _matchCount >= ExpectedCount ? SseOutcome.Pass : null;
        }

        return _matchCount > ExpectedCount
            ? new SseOutcome(SseFailureMessage.EventCountMismatch(_eventName, ExpectedCount, _matchCount, Comparison))
            : null;
    }

    /// <inheritdoc/>
    public string? Finish()
        => CountSatisfiesComparison(_matchCount) ? null : DescribeFailure();

    /// <summary>Applies the data, id, and retry narrowers, recording a near miss for the first one that
    /// fails.</summary>
    /// <param name="evt">An event of the requested type.</param>
    /// <returns><see langword="true"/> when the event passes every narrower.</returns>
    private bool PassesNarrowers(SseEvent evt)
    {
        if (DataPredicate is not null && !DataPredicate(evt.Data))
        {
            _dataNearMisses.Add(evt.Data);
            return false;
        }

        if (!PassesIdNarrower(evt.Id))
        {
            _idNearMisses.Add(evt.Id);
            return false;
        }

        if (RetryPredicate is not null && !RetryPredicate(evt.RetryMillis))
        {
            _retryNearMisses.Add(evt.RetryMillis);
            return false;
        }

        return true;
    }

    /// <summary>Applies the exact id filter or the id predicate, whichever is set.</summary>
    /// <param name="id">The frame's id.</param>
    /// <returns><see langword="true"/> when the frame passes.</returns>
    private bool PassesIdNarrower(string? id)
    {
        if (HasIdFilter)
        {
            return string.Equals(id, ExpectedId, StringComparison.Ordinal);
        }

        return IdPredicate is null || IdPredicate(id);
    }

    /// <summary>Selects the most specific failure message for an unmet expectation: event-absent,
    /// then per-narrower near-miss diagnostics, falling back to a count mismatch.</summary>
    /// <returns>The failure message.</returns>
    private string DescribeFailure()
    {
        // An event of the requested type was observed when it either fully matched or failed a
        // narrower (a near miss). With none of either, the event is genuinely absent, whether or not a
        // narrower is configured, so the event-absent diagnostic takes precedence over a count mismatch.
        var nameMatched = _matchCount + _dataNearMisses.Count + _idNearMisses.Count + _retryNearMisses.Count;
        if (nameMatched is 0)
        {
            return SseFailureMessage.EventNotFound(_eventName, _observed);
        }

        return (_matchCount is 0 ? DescribeNearMiss() : null)
            ?? SseFailureMessage.EventCountMismatch(_eventName, ExpectedCount, _matchCount, Comparison);
    }

    /// <summary>Describes why no frame matched, by the first narrower that rejected frames of the requested
    /// type.</summary>
    /// <returns>The near-miss diagnostic, or <see langword="null"/> when no narrower rejected a frame.</returns>
    private string? DescribeNearMiss()
    {
        if (_dataNearMisses.Count > 0)
        {
            return SseFailureMessage.DataPredicateNotMatched(_eventName, _dataNearMisses);
        }

        if (_idNearMisses.Count > 0)
        {
            return HasIdFilter
                ? SseFailureMessage.IdNotMatched(_eventName, ExpectedId!, _idNearMisses)
                : SseFailureMessage.IdPredicateNotMatched(_eventName, _idNearMisses);
        }

        return _retryNearMisses.Count > 0
            ? SseFailureMessage.RetryMillisPredicateNotMatched(_eventName, _retryNearMisses)
            : null;
    }

    private bool CountSatisfiesComparison(int actual)
        => Comparison switch
        {
            SseCountComparison.AtLeast => actual >= ExpectedCount,
            SseCountComparison.AtMost => actual <= ExpectedCount,
            _ => actual == ExpectedCount, // SseCountComparison.Exactly
        };
}
