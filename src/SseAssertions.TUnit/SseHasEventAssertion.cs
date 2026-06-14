using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// Fluent TUnit assertion that walks a Server-Sent Events wire-format string and verifies the
/// presence (or count) of frames matching a given event-type name. Constructed by the TUnit
/// source generator from the <c>HasSseEvent(string)</c> extension on <see cref="string"/>; the
/// narrower methods (<c>WithData</c>, <c>WithDataParsedAs&lt;T&gt;</c>, <c>WithId</c>,
/// <c>WithRetryMillis</c>) constrain which frames count and the count terminators (<c>AtLeast</c>,
/// <c>AtMost</c>, <c>Exactly</c>) close the assertion.
/// </summary>
/// <remarks>
/// The chain runs against the receiver string parsed via <see cref="SseFrameParser.Parse(string)"/>
/// inside <see cref="CheckAsync"/>. The parsed list is short-lived (per-assertion); failure
/// messages compose factories from <see cref="SseFailureMessage"/>, so consumer-authored typed
/// SSE assertions produce diagnostics identical in shape to this chain.
/// </remarks>
[AssertionExtension("HasSseEvent")]
public sealed class SseHasEventAssertion : Assertion<string>
{
    private readonly string _eventName;
    private Func<string, bool>? _dataPredicate;
    private Func<string, (bool Threw, Exception? Exception, bool Matched)>? _parsedNarrow;
    private bool _hasIdFilter;
    private string? _expectedId;
    private Func<int?, bool>? _retryPredicate;
    private SseCountComparison _comparison = SseCountComparison.AtLeast;
    private int _expectedCount = 1;

    /// <summary>Initialises the assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="eventName">The SSE event-type name to look for. Matched case-sensitively
    /// against <see cref="SseEvent.EventName"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    public SseHasEventAssertion(AssertionContext<string> context, string eventName) : base(context)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        _eventName = eventName;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".HasSseEvent({"\""}{eventName}{"\""})");
    }

    /// <summary>Narrows the assertion to frames whose <see cref="SseEvent.Data"/> satisfies the
    /// supplied predicate.</summary>
    /// <param name="predicate">The data predicate. Called with each candidate frame's
    /// <see cref="SseEvent.Data"/> value; frames for which the predicate returns
    /// <see langword="true"/> are counted.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is
    /// <see langword="null"/>.</exception>
    public SseHasEventAssertion WithData(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _dataPredicate = predicate;
        Context.ExpressionBuilder.Append(".WithData(...)");
        return this;
    }

    /// <summary>Narrows the assertion to frames whose <see cref="SseEvent.Data"/> deserializes via
    /// <paramref name="parse"/> into a <typeparamref name="T"/> that satisfies
    /// <paramref name="predicate"/>.</summary>
    /// <typeparam name="T">The type the frame data is parsed into.</typeparam>
    /// <param name="parse">The deserializer applied to each candidate frame's
    /// <see cref="SseEvent.Data"/>. Supply a reflection-free parser (for example a
    /// source-generated <c>JsonSerializer.Deserialize</c> with a <c>JsonTypeInfo&lt;T&gt;</c>) so
    /// the assertion stays AOT-compatible. If it throws, the assertion fails naming the
    /// deserializer exception and the offending data.</param>
    /// <param name="predicate">The predicate applied to the parsed value; frames for which it
    /// returns <see langword="true"/> are counted.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parse"/> or
    /// <paramref name="predicate"/> is <see langword="null"/>.</exception>
    public SseHasEventAssertion WithDataParsedAs<T>(Func<string, T> parse, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(predicate);
        _parsedNarrow = data =>
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
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithDataParsedAs<{typeof(T).Name}>(...)");
        return this;
    }

    /// <summary>Narrows the assertion to frames carrying the given <c>id:</c> directive.</summary>
    /// <param name="id">The expected <see cref="SseEvent.Id"/>. Matched case-sensitively; a frame
    /// without an <c>id:</c> directive never matches.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is
    /// <see langword="null"/>.</exception>
    public SseHasEventAssertion WithId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _hasIdFilter = true;
        _expectedId = id;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithId({"\""}{id}{"\""})");
        return this;
    }

    /// <summary>Narrows the assertion to frames whose <see cref="SseEvent.RetryMillis"/> satisfies
    /// the supplied predicate.</summary>
    /// <param name="predicate">The retry-value predicate. Called with each candidate frame's
    /// <see cref="SseEvent.RetryMillis"/> (<see langword="null"/> for frames without a
    /// <c>retry:</c> directive); frames for which it returns <see langword="true"/> are
    /// counted.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is
    /// <see langword="null"/>.</exception>
    public SseHasEventAssertion WithRetryMillis(Func<int?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _retryPredicate = predicate;
        Context.ExpressionBuilder.Append(".WithRetryMillis(...)");
        return this;
    }

    /// <summary>Expects at least <paramref name="count"/> matching frames.</summary>
    /// <param name="count">The minimum match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    public SseHasEventAssertion AtLeast(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _comparison = SseCountComparison.AtLeast;
        _expectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLeast({count})");
        return this;
    }

    /// <summary>Expects at most <paramref name="count"/> matching frames.</summary>
    /// <param name="count">The maximum match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    public SseHasEventAssertion AtMost(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _comparison = SseCountComparison.AtMost;
        _expectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtMost({count})");
        return this;
    }

    /// <summary>Expects exactly <paramref name="count"/> matching frames.</summary>
    /// <param name="count">The exact match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    public SseHasEventAssertion Exactly(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _comparison = SseCountComparison.Exactly;
        _expectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Exactly({count})");
        return this;
    }

    /// <inheritdoc/>
    protected override Task<AssertionResult> CheckAsync(EvaluationMetadata<string> metadata)
    {
        if (metadata.Exception is not null)
        {
            return Task.FromResult(AssertionResult.Failed(
                $"threw {metadata.Exception.GetType().Name}: {metadata.Exception.Message}",
                metadata.Exception));
        }

        var body = metadata.Value;
        if (body is null)
        {
            return Task.FromResult(AssertionResult.Failed("the receiver was null"));
        }

        var events = SseFrameParser.Parse(body);
        return Task.FromResult(Evaluate(events));
    }

    /// <summary>Counts the frames matching the event name and every configured narrower, then
    /// returns the pass result or the most specific failure diagnostic.</summary>
    /// <param name="events">The parsed events from the receiver.</param>
    /// <returns>The assertion result.</returns>
    private AssertionResult Evaluate(IReadOnlyList<SseEvent> events)
    {
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

            if (_parsedNarrow is not null)
            {
                var (threw, exception, matched) = _parsedNarrow(evt.Data);
                if (threw)
                {
                    return AssertionResult.Failed(SseFailureMessage.DataDeserializationFailed(_eventName, evt.Data, exception!));
                }

                if (!matched)
                {
                    dataNearMisses.Add(evt.Data);
                    continue;
                }
            }

            if (_dataPredicate is not null && !_dataPredicate(evt.Data))
            {
                dataNearMisses.Add(evt.Data);
                continue;
            }

            if (_hasIdFilter && !string.Equals(evt.Id, _expectedId, StringComparison.Ordinal))
            {
                idNearMisses.Add(evt.Id);
                continue;
            }

            if (_retryPredicate is not null && !_retryPredicate(evt.RetryMillis))
            {
                retryNearMisses.Add(evt.RetryMillis);
                continue;
            }

            matchCount++;
        }

        return CountSatisfiesComparison(matchCount)
            ? AssertionResult.Passed
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
    /// <returns>The failure result.</returns>
    private AssertionResult DescribeFailure(
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
            return AssertionResult.Failed(SseFailureMessage.EventNotFound(_eventName, events));
        }

        if (matchCount is 0 && dataNearMisses.Count > 0)
        {
            return AssertionResult.Failed(SseFailureMessage.DataPredicateNotMatched(_eventName, dataNearMisses));
        }

        if (matchCount is 0 && idNearMisses.Count > 0)
        {
            return AssertionResult.Failed(SseFailureMessage.IdNotMatched(_eventName, _expectedId!, idNearMisses));
        }

        if (matchCount is 0 && retryNearMisses.Count > 0)
        {
            return AssertionResult.Failed(SseFailureMessage.RetryMillisPredicateNotMatched(_eventName, retryNearMisses));
        }

        return AssertionResult.Failed(
            SseFailureMessage.EventCountMismatch(_eventName, _expectedCount, matchCount, _comparison));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        var label = _comparison switch
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
            _expectedCount.ToString(CultureInfo.InvariantCulture),
            " event(s) of type \"",
            _eventName,
            "\"");
    }

    private bool CountSatisfiesComparison(int actual)
        => _comparison switch
        {
            SseCountComparison.AtLeast => actual >= _expectedCount,
            SseCountComparison.AtMost => actual <= _expectedCount,
            SseCountComparison.Exactly => actual == _expectedCount,
            _ => actual == _expectedCount,
        };
}
